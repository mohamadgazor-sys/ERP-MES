# Preflight only: checks the computer and the package without starting anything. Run it a day before the presentation, and again on the presentation computer.
param([switch] $NoPause)
. (Join-Path $PSScriptRoot 'Demo-Common.ps1')
try {
    Write-Host 'ERP/MES offline demo - preflight check' -ForegroundColor Cyan
    $result = Show-Preflight (Test-DemoPreflight)
    Write-Host ''
    Out-Step 'INFO' ("Result: {0} problem(s), {1} warning(s)." -f $result.Fails, $result.Warns)
    if (-not $NoPause) { Read-Host 'Press Enter to close' }
    if ($result.Fails -gt 0) { exit 1 } else { exit 0 }
} catch {
    Out-Step 'FAIL' $_.Exception.Message
    if (-not $NoPause) { Read-Host 'Press Enter to close' }
    exit 1
}
