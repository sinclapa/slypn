# Shared helpers for setup/start/stop. Dot-source from each script:
#   . (Join-Path $PSScriptRoot '_lib.ps1')

# Resolve repo root one level up from scripts/.
$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$script:WebDir   = Join-Path $RepoRoot 'src/web'
$script:ApiDir   = Join-Path $RepoRoot 'src/api/Slypn.Api'

# Ports we standardise on (Vite default, Functions Core Tools default).
$script:WebPort = 5173
$script:ApiPort = 7071

function Write-Step($msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}
function Write-Ok($msg) {
    Write-Host "    $msg" -ForegroundColor Green
}
function Write-Warn($msg) {
    Write-Host "    $msg" -ForegroundColor Yellow
}
function Write-Err($msg) {
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
            Write-Host "    killing PID $procId ($name) on port $port" -ForegroundColor DarkGray
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
    }
}
