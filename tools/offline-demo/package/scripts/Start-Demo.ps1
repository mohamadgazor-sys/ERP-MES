# Starts the offline demo: preflight, database (created on first run), application. Run through Start-Demo.cmd.
param([switch] $NoPause)
. (Join-Path $PSScriptRoot 'Demo-Common.ps1')
try {
    Write-Host 'ERP/MES offline demo - fictional data only' -ForegroundColor Cyan
    Unblock-Package
    $result = Show-Preflight (Test-DemoPreflight)
    if ($result.Fails -gt 0) { Out-Step 'FAIL' 'The demo cannot start until the FAIL lines above are fixed.'; if (-not $NoPause) { Read-Host 'Press Enter to close' }; exit 1 }
    if ($result.Warns -gt 0) { Out-Step 'WARN' "$($result.Warns) warning(s) above. The demo will start anyway." }
    Initialize-DemoDatabase
    $process = Start-DemoApplication
    Out-Step 'OK' ('Application started (process ' + $process.Id + '). Sign in with an account from DEMO-ACCOUNTS.txt.')
    Out-Step 'INFO' ('Local database: ' + $script:DbPath)
    Start-Sleep -Seconds 4
    exit 0
} catch {
    Out-Step 'FAIL' $_.Exception.Message
    if (-not $NoPause) { Read-Host 'Press Enter to close' }
    exit 1
}
