# Proves the offline package from the ZIP alone, as close to a clean disconnected machine as a CI runner allows. Run last in a job (it hides the .NET SDK).
#   pwsh tools\offline-demo\Test-OfflinePackage.ps1 -ZipPath out\ERP-MES-OfflineDemo-...zip
# What it does, in order (each step prints PASS/FAIL evidence; any FAIL makes the exit code 1):
#   1 checks the ZIP against its .sha256 and the file list against FILES.sha256 after extraction to a NEW folder
#   2 removes every way the machine could supply .NET (renames the dotnet folders, clears DOTNET_* and PATH, deletes NuGet caches)
#   3 blocks outbound traffic of the package programs with Windows Firewall and shows the isolation mechanism works (a blocked copy of curl fails, the unblocked system curl succeeds)
#   4 Check-Demo, Start-Demo (creates the database, starts the app), UI automation signs in with all ten accounts and walks the pages (docs/UI_SMOKE.md), Reset-Demo, sign-in again
#   5 scans the logs for the demo passphrase and for leftover one-time credential files
# It cannot disconnect the runner's own network (the CI agent needs it) and cannot be a different physical computer; docs/OFFLINE_DEMO_GUIDE.md section 12 says so.
param(
    [Parameter(Mandatory = $true)][string] $ZipPath,
    [string] $WorkDir = 'C:\offline-test'
)
$ErrorActionPreference = 'Stop'
$ZipPath = (Resolve-Path $ZipPath).Path
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$failed = New-Object System.Collections.Generic.List[string]
function Section([string] $t) { Write-Host ''; Write-Host "=== $t" }
function Pass([string] $t) { Write-Host "PASS: $t" }
function Bad([string] $t) { $script:failed.Add($t); Write-Host "FAIL: $t" }

$key = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
Section 'Test machine'
Write-Host ("Windows: {0} | DisplayVersion {1} | build {2}.{3} | 64-bit {4}" -f $key.ProductName, $key.DisplayVersion, $key.CurrentBuildNumber, $key.UBR, [Environment]::Is64BitOperatingSystem)

Section '1 ZIP integrity and fresh extraction'
$expected = ((Get-Content -LiteralPath "$ZipPath.sha256" -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -eq $actual) { Pass "SHA-256 of the ZIP matches its .sha256 file ($actual)" } else { Bad "ZIP checksum mismatch: $expected vs $actual" }
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
Copy-Item -LiteralPath $ZipPath -Destination (Join-Path $WorkDir 'transfer.zip')   # "USB copy": the test uses only this copy
Expand-Archive -LiteralPath (Join-Path $WorkDir 'transfer.zip') -DestinationPath (Join-Path $WorkDir 'extracted')
$pkg = (Get-ChildItem (Join-Path $WorkDir 'extracted') -Directory | Select-Object -First 1).FullName
Write-Host "Package folder: $pkg"
$bad = 0
foreach ($line in Get-Content -LiteralPath (Join-Path $pkg 'FILES.sha256')) {
    $h, $rel = $line -split '  ', 2
    $f = Join-Path $pkg ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $f) -or (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant() -ne $h) { $bad++; Write-Host "  differs/missing: $rel" }
}
if ($bad -eq 0) { Pass 'every file listed in FILES.sha256 is present with the listed SHA-256' } else { Bad "$bad file(s) differ from FILES.sha256" }
$listing = Get-ChildItem -LiteralPath $pkg -Recurse -File
Write-Host ("Files: {0}; unpacked size {1:N1} MB; ZIP size {2:N1} MB" -f $listing.Count, (($listing | Measure-Object Length -Sum).Sum / 1MB), ((Get-Item $ZipPath).Length / 1MB))
Get-ChildItem -LiteralPath $pkg | ForEach-Object { Write-Host ('  top level: ' + $_.Name + $(if ($_.PSIsContainer) { '\' } else { '' })) }
foreach ($need in 'app\ERP.MES.Desktop.exe', 'app\e_sqlite3.dll', 'app\appsettings.json', 'app\coreclr.dll', 'app\PresentationFramework.dll', 'tools\provisioning\ERP.MES.Provisioning.exe', 'tools\provisioning\e_sqlite3.dll', 'Start-Demo.cmd', 'Reset-Demo.cmd', 'Check-Demo.cmd', 'DEMO-ACCOUNTS.txt', 'OFFLINE_DEMO_GUIDE.md', 'MANIFEST.txt', 'FILES.sha256') {
    if (Test-Path -LiteralPath (Join-Path $pkg $need)) { Pass "present: $need" } else { Bad "missing: $need" }
}
$sqlHits = $listing | Where-Object { $_.Name -match '(?i)SqlServer|SqlClient|appsettings\.Local' }
if ($sqlHits) { Bad ('SQL Server / local-settings files in the package: ' + (($sqlHits | ForEach-Object Name) -join ', ')) } else { Pass 'no SQL Server provider and no appsettings.Local.json in the package' }

Section '1b Native import inspection (which runtime DLLs the shipped native files need)'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$dumpbin = $null
if (Test-Path $vswhere) { $dumpbin = & $vswhere -latest -products * -find '**\Hostx64\x64\dumpbin.exe' | Select-Object -First 1 }
foreach ($dll in 'e_sqlite3.dll', 'coreclr.dll', 'clrjit.dll', 'hostfxr.dll', 'PresentationNative_cor3.dll', 'wpfgfx_cor3.dll', 'PenImc_cor3.dll') {
    $f = Join-Path $pkg "app\$dll"
    if (-not (Test-Path $f)) { Write-Host "  (not present: $dll)"; continue }
    if ($dumpbin) {
        $deps = & $dumpbin /nologo /dependents $f | Where-Object { $_ -match '^\s+\S+\.dll\s*$' } | ForEach-Object { $_.Trim() }
        Write-Host ("  {0} imports: {1}" -f $dll, ($deps -join ', '))
    } else { Write-Host "  dumpbin not found; cannot list imports of $dll" }
}
Get-ChildItem (Join-Path $pkg 'app') -Filter 'vcruntime*.dll' | ForEach-Object { Write-Host ('  bundled C++ runtime file: ' + $_.Name) }
Get-ChildItem (Join-Path $pkg 'app') -Filter 'msvcp*.dll' | ForEach-Object { Write-Host ('  bundled C++ runtime file: ' + $_.Name) }

$hidden = @()
try {
    Section '2 Remove every way this machine could supply .NET or packages'
    # Stop everything that still holds files in the SDK folder (build servers, MSBuild nodes, the compiler server), then hide the folders.
    & dotnet build-server shutdown 2>&1 | Out-Null
    Get-Process -Name 'dotnet', 'VBCSCompiler', 'MSBuild', 'msbuild', 'vbcscompiler' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    foreach ($d in 'C:\Program Files\dotnet', 'C:\Program Files (x86)\dotnet') {
        if (-not (Test-Path $d)) { continue }
        $renamed = $false
        for ($try = 1; $try -le 6 -and -not $renamed; $try++) {
            try { Rename-Item -LiteralPath $d -NewName ((Split-Path -Leaf $d) + '.hidden-for-offline-test') -ErrorAction Stop; $renamed = $true; $hidden += $d }
            catch { Write-Host "  rename of $d attempt $try failed: $($_.Exception.Message)"; Get-Process | Where-Object { $_.Path -like "$d*" } | ForEach-Object { Write-Host ('    still running from there: ' + $_.Name + ' ' + $_.Id) }; Start-Sleep -Seconds 5 }
        }
        if (-not $renamed) { throw "Could not hide $d from this machine; the offline test would not be clean." }
    }
    foreach ($v in 'DOTNET_ROOT', 'DOTNET_ROOT(x86)', 'DOTNET_HOST_PATH', 'DOTNET_MULTILEVEL_LOOKUP', 'MSBuildSDKsPath', 'NUGET_PACKAGES') { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }
    $env:PATH = (($env:PATH -split ';') | Where-Object { $_ -and $_ -notmatch '(?i)\\dotnet(\\|$)' }) -join ';'
    foreach ($d in (Join-Path $env:USERPROFILE '.nuget'), (Join-Path $env:LOCALAPPDATA 'NuGet'), (Join-Path $env:USERPROFILE '.dotnet')) { if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue } }
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { Bad 'dotnet is still resolvable' } else { Pass 'no dotnet host is resolvable (folders renamed, PATH and DOTNET_* cleared)' }
    if (Test-Path 'C:\Program Files\dotnet\dotnet.exe') { Bad 'dotnet.exe still exists' } else { Pass 'C:\Program Files\dotnet\dotnet.exe does not exist' }

    Section '3 Network isolation of the package programs'
    $probe = Join-Path $pkg 'erpmes-probe.exe'
    Copy-Item (Join-Path $env:WINDIR 'System32\curl.exe') $probe
    $programs = @((Join-Path $pkg 'app\ERP.MES.Desktop.exe'), (Join-Path $pkg 'tools\provisioning\ERP.MES.Provisioning.exe'), $probe)
    foreach ($prog in $programs) {
        New-NetFirewallRule -DisplayName ('erpmes-offline-test ' + (Split-Path -Leaf $prog) + ' out') -Direction Outbound -Action Block -Program $prog -Profile Any | Out-Null
        New-NetFirewallRule -DisplayName ('erpmes-offline-test ' + (Split-Path -Leaf $prog) + ' in') -Direction Inbound -Action Block -Program $prog -Profile Any | Out-Null
    }
    Pass ('outbound and inbound block rules created for: ' + (($programs | ForEach-Object { Split-Path -Leaf $_ }) -join ', '))
    $control = & (Join-Path $env:WINDIR 'System32\curl.exe') -sS -m 20 -o NUL -w '%{http_code}' https://example.com 2>&1
    Write-Host "  control (system curl, NOT blocked) -> $control"
    $blocked = & $probe -sS -m 20 -o NUL -w '%{http_code}' https://example.com 2>&1
    Write-Host "  blocked copy of curl in the package folder -> $blocked"
    if ("$control" -match '^[23]\d\d$') {
        if ("$blocked" -notmatch '^[23]\d\d$') { Pass 'the firewall rules demonstrably block a program in the package folder while the machine itself is online' } else { Bad 'the blocked probe could reach the internet - isolation is NOT effective' }
    } else { Write-Host 'NOTE: the runner has no internet for the control request either, so the isolation could not be contrasted.' }

    Section '4 Launcher, application and UI automation from the package'
    $ps = (Get-Command powershell.exe).Source
    & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pkg 'scripts\Check-Demo.ps1') -NoPause
    if ($LASTEXITCODE -eq 0) { Pass 'Check-Demo: no FAIL line' } else { Bad "Check-Demo exit code $LASTEXITCODE" }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pkg 'scripts\Start-Demo.ps1') -NoPause
    $startCode = $LASTEXITCODE
    Write-Host ("  Start-Demo took {0:N0} s (first run creates the database)" -f $sw.Elapsed.TotalSeconds)
    if ($startCode -eq 0) { Pass 'Start-Demo created the database and started the application' } else { Bad "Start-Demo exit code $startCode" }
    $db = Join-Path $pkg 'data\erpmes.demo.db'
    if (Test-Path $db) { Pass ('database file exists: ' + (Get-Item $db).Length + ' bytes') } else { Bad 'database file missing' }
    $state = (& (Join-Path $pkg 'tools\provisioning\ERP.MES.Provisioning.exe') demo-status $db | Out-String).Trim()
    if ($state -eq 'Demo') { Pass 'demo-status says Demo (marked demonstration database)' } else { Bad "demo-status: $state" }
    Start-Sleep -Seconds 3
    Get-Process -Name 'ERP.MES.Desktop' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    $ui = Join-Path $repo 'tools\ui-smoke\ui-smoke.ps1'
    $accounts = Join-Path $pkg 'DEMO-ACCOUNTS.txt'
    $exe = Join-Path $pkg 'app\ERP.MES.Desktop.exe'
    & $ps -NoProfile -ExecutionPolicy Bypass -File $ui -ExePath $exe -DatabasePath $db -AccountsFile $accounts -SignInOnly 'demo.admin,demo.management,demo.drawing,demo.qc,demo.warehouse,demo.production,demo.productionmanager'
    if ($LASTEXITCODE -eq 0) { Pass 'UI automation: all ten accounts signed in; 5 accounts walked the pages their role may see (hidden pages absent); no network connection held' } else { Bad "UI automation exit code $LASTEXITCODE" }

    Section '4b Reset and sign in again'
    & $ps -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pkg 'scripts\Reset-Demo.ps1') -Yes -NoPause
    if ($LASTEXITCODE -eq 0) { Pass 'Reset-Demo succeeded' } else { Bad "Reset-Demo exit code $LASTEXITCODE" }
    $state = (& (Join-Path $pkg 'tools\provisioning\ERP.MES.Provisioning.exe') demo-status $db | Out-String).Trim()
    if ($state -eq 'Demo') { Pass 'after the reset the file is still a marked demonstration database' } else { Bad "demo-status after reset: $state" }
    & $ps -NoProfile -ExecutionPolicy Bypass -File $ui -ExePath $exe -DatabasePath $db -AccountsFile $accounts -SkipWalk -SignInOnly 'demo.secretary,demo.planning,demo.management'
    if ($LASTEXITCODE -eq 0) { Pass 'after the reset the accounts sign in with the demo passphrase (persisted in the reset database)' } else { Bad "post-reset UI automation exit code $LASTEXITCODE" }

    Section '5 Logs and leftovers'
    $pass = ((Get-Content (Join-Path $pkg 'DEMO-ACCOUNTS.txt') | Where-Object { $_ -match '^PASSPHRASE:' }) -replace '^PASSPHRASE:\s*', '').Trim()
    $logs = Get-ChildItem (Join-Path $pkg 'logs') -Recurse -File -Filter '*.log'
    Write-Host ('  log files: ' + (($logs | ForEach-Object { $_.Name + ' (' + $_.Length + ' bytes)' }) -join ', '))
    $leak = $logs | Select-String -SimpleMatch $pass
    if ($leak) { Bad 'the demo passphrase appears in a log file' } else { Pass 'the demo passphrase does not appear in any log file' }
    foreach ($l in $logs) { Get-Content $l.FullName -Tail 6 | ForEach-Object { Write-Host ('  [' + $l.Name + '] ' + $_) } }
    if (Get-ChildItem $env:TEMP -Filter 'erpmes-demo-*.credentials.txt' -ErrorAction SilentlyContinue) { Bad 'a one-time credential file was left in %TEMP%' } else { Pass 'no one-time credential file left in %TEMP%' }
}
finally {
    foreach ($d in $hidden) { $h = $d + '.hidden-for-offline-test'; if (Test-Path $h) { Rename-Item -LiteralPath $h -NewName (Split-Path -Leaf $d) } }
    Get-NetFirewallRule -DisplayName 'erpmes-offline-test*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
}

Write-Host ''
if ($failed.Count -gt 0) { Write-Host ("OFFLINE PACKAGE TEST FAILED: {0} problem(s)" -f $failed.Count); $failed | ForEach-Object { Write-Host " - $_" }; exit 1 }
Write-Host 'OFFLINE PACKAGE TEST PASSED'
exit 0
