# Packs the logs and the computer facts into a ZIP on the Desktop that can be e-mailed to the developer. It never includes the database, DEMO-ACCOUNTS.txt or any password.
param([switch] $NoPause)
. (Join-Path $PSScriptRoot 'Demo-Common.ps1')
try {
    $stage = Join-Path $env:TEMP ('erpmes-diag-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    if (Test-Path -LiteralPath $script:LogDir) { Copy-Item -LiteralPath $script:LogDir -Destination (Join-Path $stage 'logs') -Recurse }
    $os = Get-WindowsFacts
    $facts = @("Windows: $($os.Name) $($os.Display) build $($os.Build).$($os.Ubr)", "64-bit OS: $($os.Is64)", "PowerShell: $($PSVersionTable.PSVersion)", "Package folder: $($script:Root)",
        ("Database present: " + (Test-Path -LiteralPath $script:DbPath)), ("Free space MB: " + [math]::Round((New-Object System.IO.DriveInfo ([System.IO.Path]::GetPathRoot($script:Root))).AvailableFreeSpace / 1MB)))
    Set-Content -LiteralPath (Join-Path $stage 'computer.txt') -Value $facts
    $pre = Test-DemoPreflight | ForEach-Object { '[{0}] {1}' -f $_.Level, $_.Text }
    Set-Content -LiteralPath (Join-Path $stage 'preflight.txt') -Value $pre
    $target = Join-Path ([Environment]::GetFolderPath('Desktop')) ('erpmes-diagnostics-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $target
    Remove-Item -LiteralPath $stage -Recurse -Force
    Out-Step 'OK' ('Diagnostics written to ' + $target + ' (no database, no passwords).')
    if (-not $NoPause) { Read-Host 'Press Enter to close' }
} catch {
    Out-Step 'FAIL' $_.Exception.Message
    if (-not $NoPause) { Read-Host 'Press Enter to close' }
    exit 1
}
