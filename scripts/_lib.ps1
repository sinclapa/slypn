# Shared helpers for setup/start/stop. Dot-source from each script:
#   . (Join-Path $PSScriptRoot '_lib.ps1')

# Resolve repo root one level up from scripts/.
$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$script:WebDir   = Join-Path $RepoRoot 'src/web'
$script:ApiDir   = Join-Path $RepoRoot 'src/api/Slypn.Api'

# Ports we standardise on (Vite default, Functions Core Tools default).
$script:WebPort = 5173
$script:ApiPort = 7071

# Emulators (Docker).
$script:AzuriteContainer = 'slypn-azurite'
$script:AzuriteImage     = 'mcr.microsoft.com/azure-storage/azurite:latest'
$script:AzuriteBlobPort  = 10000
$script:AzuriteQueuePort = 10001
$script:AzuriteTablePort = 10002

function Show-Step($msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}
function Show-Ok($msg) {
    Write-Host "    $msg" -ForegroundColor Green
}
function Show-Warn($msg) {
    Write-Host "    $msg" -ForegroundColor Yellow
}
function Show-Err($msg) {
    Write-Host "    $msg" -ForegroundColor Red
}

function Test-Port($port) {
    return [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

function Stop-Port($port) {
    $procIds = (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue).OwningProcess
    foreach ($procId in ($procIds | Sort-Object -Unique)) {
        if ($procId) {
            $name = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName
            Write-Output "    killing PID $procId ($name) on port $port"
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
    }
}

# -- Docker helpers ---------------------------------------------------------
function Test-DockerRunning {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { return $false }
    docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Test-ContainerRunning($name) {
    $found = docker ps --filter "name=^/$name$" --format '{{.Names}}' 2>$null
    return [bool]($found -and $found.Trim() -eq $name)
}

function Test-ContainerExists($name) {
    $found = docker ps -a --filter "name=^/$name$" --format '{{.Names}}' 2>$null
    return [bool]($found -and $found.Trim() -eq $name)
}

function Wait-Port($port, $timeoutSec) {
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $timeoutSec) {
        if (Test-Port $port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

# -- e2e backend (Azurite + Functions host) ----------------------------------
# Best-effort: starts whatever isn't already running so Playwright's UI-only
# views get real data instead of ECONNREFUSED. Reuses an already-running
# Azurite container / func host untouched; only stops what it itself started.
function Start-E2eBackend($logDir) {
    if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
    $result = [pscustomobject]@{ StartedAzurite = $false; ApiProcess = $null }

    if (Test-DockerRunning) {
        if (Test-ContainerRunning $AzuriteContainer) {
            Show-Ok "$AzuriteContainer already running"
        } else {
            if (Test-ContainerExists $AzuriteContainer) {
                docker start $AzuriteContainer | Out-Null
            } else {
                docker run -d --name $AzuriteContainer `
                    -p "${AzuriteBlobPort}:10000" `
                    -p "${AzuriteQueuePort}:10001" `
                    -p "${AzuriteTablePort}:10002" `
                    $AzuriteImage | Out-Null
            }
            $result.StartedAzurite = $true
            if ((Wait-Port $AzuriteBlobPort 30) -and (Wait-Port $AzuriteTablePort 30)) {
                Show-Ok "$AzuriteContainer ready"
            } else {
                Show-Warn "Azurite didn't come up in time — e2e will run without a live API."
            }
        }
    } else {
        Show-Warn 'Docker not available — e2e will run without a live API.'
    }

    if (Test-Port $ApiPort) {
        Show-Ok "API already running on $ApiPort"
        return $result
    }

    $localSettings = Join-Path $ApiDir 'local.settings.json'
    if (-not (Test-Path $localSettings)) {
        $sample = Join-Path $ApiDir 'local.settings.sample.json'
        if (Test-Path $sample) { Copy-Item $sample $localSettings }
    }

    $funcCmd = Get-Command func -ErrorAction SilentlyContinue
    if (-not $funcCmd) {
        $candidate = Join-Path $env:LOCALAPPDATA 'AzureFunctionsCoreTools/func.exe'
        if (Test-Path $candidate) { $funcCmd = [pscustomobject]@{ Source = $candidate } }
    }
    if (-not $funcCmd) {
        Show-Warn 'func not on PATH — e2e will run without a live API.'
        return $result
    }

    $apiLog = Join-Path $logDir 'api.log'
    $result.ApiProcess = Start-Process -FilePath $funcCmd.Source -ArgumentList 'start' `
        -WorkingDirectory $ApiDir `
        -RedirectStandardOutput $apiLog `
        -RedirectStandardError  "$apiLog.err" `
        -WindowStyle Hidden -PassThru

    if (Wait-Port $ApiPort 90) {
        Show-Ok "func ready on http://localhost:$ApiPort/ for e2e"
    } else {
        Show-Warn "func didn't start on $ApiPort within 90s — e2e will run without a live API. See $apiLog."
    }
    return $result
}

function Stop-E2eBackend($backend) {
    if ($backend.ApiProcess) {
        Stop-Process -Id $backend.ApiProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($backend.StartedAzurite) {
        docker stop $AzuriteContainer | Out-Null
    }
}
