# Restores the demonstration data to its known starting state. Only the demo database file data\erpmes.demo.db is replaced, and only if it is a marked demonstration database.
param([switch] $Yes, [switch] $NoPause)
. (Join-Path $PSScriptRoot 'Demo-Common.ps1')
try {
    if (Get-Process -Name 'ERP.MES.Desktop' -ErrorAction SilentlyContinue) { throw 'Close the ERP/MES application first, then run Reset-Demo again.' }
    if (-not (Test-Path -LiteralPath $script:Tool)) { throw 'tools\provisioning\ERP.MES.Provisioning.exe is missing - extract the whole ZIP again.' }
    New-Item -ItemType Directory -Force -Path $script:DataDir | Out-Null
    if (-not $Yes) {
        Write-Host 'This replaces ALL data in the demo database with the fictional starting data. Your other files are not touched.' -ForegroundColor Yellow
        if ((Read-Host 'Type YES to continue') -cne 'YES') { Out-Step 'INFO' 'Cancelled. Nothing was changed.'; exit 0 }
    }
    if (Test-Path -LiteralPath $script:DbPath) {
        Out-Step 'INFO' 'Resetting the demonstration database (about 20-60 seconds)...'
        $code = Invoke-Tool @('demo-reset', $script:DbPath, '--yes', '--credentials', (New-OneTimeFilePath), '--presentation')
    } else {
        Out-Step 'INFO' 'No database yet; creating it (about 20-60 seconds)...'
        $code = Invoke-Tool @('demo-init', $script:DbPath, '--credentials', (New-OneTimeFilePath), '--presentation')
    }
    if ($code -ne 0) { throw "The reset failed (exit code $code); the previous database, if any, was kept. See logs\launcher-*.log." }
    Out-Step 'OK' 'The demonstration is back at its starting state. All accounts use the passphrase in DEMO-ACCOUNTS.txt.'
    if (-not $NoPause) { Read-Host 'Press Enter to close' }
    exit 0
} catch {
    Out-Step 'FAIL' $_.Exception.Message
    if (-not $NoPause) { Read-Host 'Press Enter to close' }
    exit 1
}
