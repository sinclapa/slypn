#requires -Version 7
<#
.SYNOPSIS
  Runs all SLYPN test suites, prints a combined test summary plus a branch +
  total coverage report, and rates coverage:

      fail          < 80%
      satisfactory  80–89%
      good          >= 90%

.DESCRIPTION
  Suites:
    - API (.NET)       : `dotnet test` with coverlet ("XPlat Code Coverage").
    - UI unit (Vitest) : src/web component/logic tests with v8 coverage.
    - UI e2e (Playwright): src/web browser tests (pass/fail only — not
                           instrumented for coverage).

  Coverage is read from each suite's Cobertura report (branch-rate / line-rate)
  and combined into an overall total. The combined total (line) coverage drives
  the pass/fail gate.

  Exit codes: 0 = all good, 1 = a test failed, 2 = combined coverage below the
  fail threshold.

.PARAMETER SkipApi
  Skip the .NET API tests (and their coverage).

.PARAMETER SkipUnit
  Skip the Vitest UI unit tests (and their coverage).

.PARAMETER SkipE2e
  Skip the Playwright UI e2e tests.

.PARAMETER FailUnder
  Coverage percent below which the run fails (default 80).

.PARAMETER GoodAtLeast
  Coverage percent at/above which coverage is rated "good" (default 90).

.EXAMPLE
  .\scripts\testLocal.ps1

.EXAMPLE
  .\scripts\testLocal.ps1 -SkipE2e            # unit + API + coverage, no browser

.EXAMPLE
  .\scripts\testLocal.ps1 -FailUnder 70 -GoodAtLeast 85
#>

[CmdletBinding()]
param(
    [switch] $SkipApi,
    [switch] $SkipUnit,
    [switch] $SkipE2e,
    [int]    $FailUnder   = 80,
    [int]    $GoodAtLeast = 90
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_lib.ps1')

$apiTestsProj     = Join-Path $RepoRoot 'src/api/Slypn.Api.Tests/Slypn.Api.Tests.csproj'
$apiRunSettings   = Join-Path $RepoRoot 'src/api/Slypn.Api.Tests/coverlet.runsettings'
$resultsDir   = Join-Path $RepoRoot '.testresults'
$apiCovDir    = Join-Path $resultsDir 'api-coverage'
$webCovDir    = Join-Path $resultsDir 'web-coverage'
$vitestJson   = Join-Path $resultsDir 'vitest-results.json'
$pwJsonPath   = Join-Path $resultsDir 'playwright-results.json'

$inv = [System.Globalization.CultureInfo]::InvariantCulture

function Get-Rating([double]$pct) {
    if ($pct -ge $GoodAtLeast) { 'good' }
    elseif ($pct -ge $FailUnder) { 'satisfactory' }
    else { 'fail' }
}

function Write-Rated([string]$label, [double]$pct, [string]$detail) {
    $rating = Get-Rating $pct
    $color  = switch ($rating) { 'good' { 'Green' } 'satisfactory' { 'Yellow' } default { 'Red' } }
    Write-Host ("    {0,-22}{1,6:N1}%  [{2}]  {3}" -f $label, $pct, $rating.ToUpper(), $detail) -ForegroundColor $color
}

# Reads the newest Cobertura report under $dir into a metrics object, or $null.
function Read-Cobertura([string]$name, [string]$dir) {
    $file = Get-ChildItem $dir -Recurse -Filter '*cobertura*.xml' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $file) { return $null }
    [xml]$x = Get-Content $file.FullName -Raw
    [pscustomobject]@{
        Name            = $name
        File            = $file.FullName
        LinePct         = [double]::Parse($x.coverage.'line-rate',   $inv) * 100
        BranchPct       = [double]::Parse($x.coverage.'branch-rate', $inv) * 100
        LinesCovered    = [int]$x.coverage.'lines-covered'
        LinesValid      = [int]$x.coverage.'lines-valid'
        BranchesCovered = [int]$x.coverage.'branches-covered'
        BranchesValid   = [int]$x.coverage.'branches-valid'
    }
}

# Accumulators
$suites    = @()   # rows for the test summary
$covSources = @()  # Cobertura metrics per suite
$runError  = $false

if (Test-Path $resultsDir) { Remove-Item $resultsDir -Recurse -Force }
New-Item -ItemType Directory -Path $resultsDir | Out-Null

# ── API tests + coverage ──────────────────────────────────────────────────────
if (-not $SkipApi) {
    Write-Step 'API tests (.NET) with coverage'
    & dotnet test $apiTestsProj `
        --settings $apiRunSettings `
        --collect:"XPlat Code Coverage" `
        --results-directory $apiCovDir `
        --nologo -v q 2>&1 | Tee-Object -Variable apiOut
    $apiExit = $LASTEXITCODE

    $passed = 0; $failed = 0; $skipped = 0
    foreach ($m in [regex]::Matches((($apiOut | Out-String)), 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)')) {
        $failed  += [int]$m.Groups[1].Value
        $passed  += [int]$m.Groups[2].Value
        $skipped += [int]$m.Groups[3].Value
    }
    if ($apiExit -ne 0 -and $failed -eq 0) { $runError = $true; $failed = [Math]::Max($failed, 1) }
    $suites += [pscustomobject]@{ Name = 'API (.NET)'; Passed = $passed; Failed = $failed; Skipped = $skipped }

    $cov = Read-Cobertura 'API (.NET)' $apiCovDir
    if ($cov) { $covSources += $cov } else { Write-Warn 'No API Cobertura report produced.' }
} else {
    Write-Warn 'Skipping API tests (-SkipApi).'
}

# ── UI unit tests (Vitest) + coverage ─────────────────────────────────────────
if (-not $SkipUnit) {
    Write-Step 'UI unit tests (Vitest) with coverage'
    Push-Location $WebDir
    try {
        & npx vitest run --coverage `
            "--coverage.reportsDirectory=$webCovDir" `
            --reporter=default --reporter=json `
            "--outputFile.json=$vitestJson"
        $unitExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    $passed = 0; $failed = 0; $skipped = 0
    if (Test-Path $vitestJson) {
        $v = Get-Content $vitestJson -Raw | ConvertFrom-Json
        $passed  = [int]$v.numPassedTests
        $failed  = [int]$v.numFailedTests
        $skipped = [int]$v.numPendingTests + [int]$v.numTodoTests
    } elseif ($unitExit -ne 0) {
        $runError = $true; $failed = 1
    }
    $suites += [pscustomobject]@{ Name = 'UI unit'; Passed = $passed; Failed = $failed; Skipped = $skipped }

    $cov = Read-Cobertura 'UI (Vitest)' $webCovDir
    if ($cov) { $covSources += $cov } else { Write-Warn 'No UI Cobertura report produced.' }
} else {
    Write-Warn 'Skipping UI unit tests (-SkipUnit).'
}

# ── UI e2e tests (Playwright) ─────────────────────────────────────────────────
if (-not $SkipE2e) {
    Write-Step 'UI e2e tests (Playwright)'
    Push-Location $WebDir
    try {
        & npx playwright install chromium 2>&1 | Out-Null   # idempotent
        $env:PLAYWRIGHT_JSON_OUTPUT_NAME = $pwJsonPath
        & npx playwright test "--reporter=line,json"
        $e2eExit = $LASTEXITCODE
        Remove-Item Env:PLAYWRIGHT_JSON_OUTPUT_NAME -ErrorAction SilentlyContinue
    } finally {
        Pop-Location
    }

    $passed = 0; $failed = 0; $skipped = 0; $flaky = 0
    if (Test-Path $pwJsonPath) {
        $pw = Get-Content $pwJsonPath -Raw | ConvertFrom-Json
        if ($pw.PSObject.Properties.Name -contains 'stats') {
            $passed  = [int]$pw.stats.expected
            $failed  = [int]$pw.stats.unexpected
            $skipped = [int]$pw.stats.skipped
            $flaky   = [int]$pw.stats.flaky
        }
    } elseif ($e2eExit -ne 0) {
        $runError = $true; $failed = 1
    }
    $suites += [pscustomobject]@{ Name = 'UI e2e'; Passed = $passed; Failed = $failed; Skipped = $skipped; Flaky = $flaky }
} else {
    Write-Warn 'Skipping UI e2e tests (-SkipE2e).'
}

# ── Test summary ──────────────────────────────────────────────────────────────
$totalPassed  = ($suites | Measure-Object -Property Passed  -Sum).Sum
$totalFailed  = ($suites | Measure-Object -Property Failed  -Sum).Sum
$totalSkipped = ($suites | Measure-Object -Property Skipped -Sum).Sum

Write-Host ''
Write-Step 'Test summary'
foreach ($s in $suites) {
    $flakyNote = if ($s.PSObject.Properties.Name -contains 'Flaky' -and $s.Flaky -gt 0) { "   (flaky $($s.Flaky))" } else { '' }
    Write-Host ("    {0,-12} passed {1,4}   failed {2,4}   skipped {3,4}{4}" -f $s.Name, $s.Passed, $s.Failed, $s.Skipped, $flakyNote)
}
Write-Host ('    ' + ('-' * 50)) -ForegroundColor DarkGray
$totalColor = if ($totalFailed -eq 0 -and -not $runError) { 'Green' } else { 'Red' }
Write-Host ("    {0,-12} passed {1,4}   failed {2,4}   skipped {3,4}" -f 'TOTAL', $totalPassed, $totalFailed, $totalSkipped) -ForegroundColor $totalColor

# ── Coverage report ───────────────────────────────────────────────────────────
Write-Host ''
Write-Step "Coverage  ·  fail <$FailUnder%  ·  satisfactory <$GoodAtLeast%  ·  good >=$GoodAtLeast%"

$combinedLinePct = $null
if ($covSources.Count -gt 0) {
    foreach ($c in $covSources) {
        Write-Host "    $($c.Name)" -ForegroundColor White
        Write-Rated '  branch'       $c.BranchPct ("{0}/{1} branches" -f $c.BranchesCovered, $c.BranchesValid)
        Write-Rated '  total (line)' $c.LinePct   ("{0}/{1} lines"    -f $c.LinesCovered, $c.LinesValid)
    }

    $lc = ($covSources | Measure-Object -Property LinesCovered    -Sum).Sum
    $lv = ($covSources | Measure-Object -Property LinesValid      -Sum).Sum
    $bc = ($covSources | Measure-Object -Property BranchesCovered -Sum).Sum
    $bv = ($covSources | Measure-Object -Property BranchesValid   -Sum).Sum
    $combinedLinePct   = if ($lv -gt 0) { $lc / $lv * 100 } else { 0 }
    $combinedBranchPct = if ($bv -gt 0) { $bc / $bv * 100 } else { 0 }

    Write-Host '    COMBINED' -ForegroundColor White
    Write-Rated '  branch'       $combinedBranchPct ("{0}/{1} branches" -f $bc, $bv)
    Write-Rated '  total (line)' $combinedLinePct   ("{0}/{1} lines"    -f $lc, $lv)
} else {
    Write-Warn 'Coverage not measured (no instrumented suite ran).'
}

# ── Changed-file (branch diff) coverage ──────────────────────────────────────
Write-Host ''
Write-Step 'Changed-file coverage  (current branch vs main)'

$diffBase    = if (git rev-parse --verify origin/main 2>$null) { 'origin/main' } else { 'main' }
$changedFiles = (git diff "$diffBase...HEAD" --name-only 2>$null) -split "`n" |
    Where-Object { $_ -match '\.(cs|ts|vue)$' -and $_ -notmatch '\.(spec|test)\.' -and $_ -notmatch '[\\/]test[\\/]' }

if (-not $changedFiles) {
    Write-Warn 'No changed source files detected on this branch.'
} elseif ($covSources.Count -eq 0) {
    Write-Warn 'No coverage data — run without -SkipApi/-SkipUnit to instrument.'
} else {
    $allXml = foreach ($c in $covSources) {
        if (Test-Path $c.File) { [xml](Get-Content $c.File -Raw) }
    }

    $anyFound = $false
    foreach ($changed in $changedFiles) {
        $norm = $changed -replace '\\', '/'

        $classNode = $null
        foreach ($x in $allXml) {
            $classNode = $x.SelectNodes('//class') |
                Where-Object { ($_.filename -replace '\\', '/') -like "*$norm" } |
                Select-Object -First 1
            if ($classNode) { break }
        }
        if (-not $classNode) { continue }
        $anyFound = $true

        $lineNodes = $classNode.SelectNodes('lines/line')
        $lv = $lineNodes.Count
        $lc = ($lineNodes | Where-Object { [int]$_.hits -gt 0 }).Count
        $linePct = if ($lv -gt 0) { $lc / $lv * 100 } else { 100.0 }

        $bv = 0; $bc = 0
        foreach ($ln in ($lineNodes | Where-Object { $_.branch -eq 'true' })) {
            if ($ln.'condition-coverage' -match '\((\d+)/(\d+)\)') {
                $bc += [int]$Matches[1]; $bv += [int]$Matches[2]
            }
        }
        $brStr = if ($bv -gt 0) { "  branch $([math]::Round($bc/$bv*100,1))% ($bc/$bv)" } else { '' }
        $label = ($norm -replace '^src/(api|web)/', '') -replace 'Slypn\.Api[\\/]', ''
        Write-Rated "  $label" $linePct ("{0}/{1} lines{2}" -f $lc, $lv, $brStr)
    }
    if (-not $anyFound) {
        Write-Warn 'Changed files not found in coverage data (test/config files only?).'
    }
}

# ── Verdict / exit ────────────────────────────────────────────────────────────
Write-Host ''
if ($totalFailed -gt 0 -or $runError) {
    Write-Err 'FAIL — one or more tests failed.'
    exit 1
}
if ($null -ne $combinedLinePct -and $combinedLinePct -lt $FailUnder) {
    Write-Err ("FAIL — combined coverage {0:N1}% is below the {1}% threshold." -f $combinedLinePct, $FailUnder)
    exit 2
}
Write-Ok 'PASS — all tests green and coverage gate met.'
exit 0
