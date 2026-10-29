# Shared helpers of the offline demo launcher. Windows PowerShell 5.1 compatible (built into Windows 10/11). Nothing here needs the internet.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Root = Split-Path -Parent $PSScriptRoot
$script:DataDir = Join-Path $script:Root 'data'
$script:LogDir = Join-Path $script:Root 'logs'
$script:DbPath = Join-Path $script:DataDir 'erpmes.demo.db'
$script:Exe = Join-Path $script:Root 'app\ERP.MES.Desktop.exe'
$script:Tool = Join-Path $script:Root 'tools\provisioning\ERP.MES.Provisioning.exe'
$script:AccountsFile = Join-Path $script:Root 'DEMO-ACCOUNTS.txt'
$script:LauncherLog = Join-Path $script:LogDir ('launcher-' + (Get-Date -Format 'yyyyMMdd') + '.log')

function Write-DemoLog([string] $Message) {
    try {
        if (-not (Test-Path -LiteralPath $script:LogDir)) { New-Item -ItemType Directory -Force -Path $script:LogDir | Out-Null }
        Add-Content -LiteralPath $script:LauncherLog -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $Message)
    } catch { }
}

function Out-Step([string] $Level, [string] $Message) {
    $color = switch ($Level) { 'OK' { 'Green' } 'WARN' { 'Yellow' } 'FAIL' { 'Red' } default { 'Gray' } }
    Write-Host ('[{0}] {1}' -f $Level, $Message) -ForegroundColor $color
    Write-DemoLog ('[{0}] {1}' -f $Level, $Message)
}

# The demo passphrase is demo-only and fictional: it is read from DEMO-ACCOUNTS.txt (line "PASSPHRASE: ...") and passed to the provisioning tool by environment variable, never on a command line.
function Get-DemoPassphrase {
    if (-not (Test-Path -LiteralPath $script:AccountsFile)) { throw "DEMO-ACCOUNTS.txt is missing from the package folder." }
    $line = Get-Content -LiteralPath $script:AccountsFile -Encoding UTF8 | Where-Object { $_ -match '^PASSPHRASE:\s*(\S.*)$' } | Select-Object -First 1
    if (-not $line) { throw "DEMO-ACCOUNTS.txt has no 'PASSPHRASE:' line." }
    return ($line -replace '^PASSPHRASE:\s*', '').Trim()
}

function Get-WindowsFacts {
    $key = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $build = [int]$key.CurrentBuildNumber
    $display = ''
    if ($key.PSObject.Properties.Name -contains 'DisplayVersion') { $display = [string]$key.DisplayVersion } elseif ($key.PSObject.Properties.Name -contains 'ReleaseId') { $display = [string]$key.ReleaseId }
    $name = if ($build -ge 22000) { 'Windows 11' } else { [string]$key.ProductName }
    return [pscustomobject]@{ Name = $name; Display = $display; Build = $build; Ubr = $key.UBR; Is64 = [Environment]::Is64BitOperatingSystem; Arch = $env:PROCESSOR_ARCHITECTURE }
}

# Returns the list of findings; each is @{Level='OK|WARN|FAIL'; Text=...}. FAIL means the demo is not expected to start.
function Test-DemoPreflight {
    $findings = New-Object System.Collections.Generic.List[object]
    function Add([string] $l, [string] $t) { $findings.Add([pscustomobject]@{ Level = $l; Text = $t }) }

    $os = Get-WindowsFacts
    $osText = '{0} {1} (build {2}.{3}), {4}' -f $os.Name, $os.Display, $os.Build, $os.Ubr, $(if ($os.Is64) { '64-bit' } else { '32-bit' })
    if (-not $os.Is64) { Add 'FAIL' ('This package is for 64-bit (x64) Windows. This computer: ' + $osText) }
    elseif ($os.Build -ge 26100) { Add 'OK' ('Windows version ' + $osText + ' - listed by Microsoft as supported for .NET 10 (Windows 11 24H2 or newer).') }
    elseif ($os.Build -eq 22631) { Add 'WARN' ('Windows version ' + $osText + ' - Windows 11 23H2: .NET 10 lists it only for Enterprise/Education editions and Microsoft ended Home/Pro servicing. It is likely to work but is not an officially supported combination for Home/Pro. Prefer upgrading to 24H2 or newer before the presentation.') }
    elseif ($os.Build -ge 22000) { Add 'WARN' ('Windows version ' + $osText + ' - this Windows 11 version is NOT in the .NET 10 supported list (22H2 is out of Microsoft servicing). The demo may still run, but it is not officially supported: upgrade to Windows 11 24H2 or newer before the presentation and run Check-Demo again.') }
    else { Add 'WARN' ('Windows version ' + $osText + ' - older than Windows 11. Windows 10 is supported by .NET 10 only for specific servicing channels; use Windows 11 24H2 or newer for the presentation.') }

    if ($script:Root.StartsWith('\\')) { Add 'FAIL' ('The package is on a network path (' + $script:Root + '). Copy it to a local drive such as C:\ERP-MES-Demo.') }
    elseif ($script:Root.Length -gt 120) { Add 'WARN' ('The folder path is long (' + $script:Root.Length + ' characters); move the package nearer to the drive root, for example C:\ERP-MES-Demo.') }
    else { Add 'OK' ('Local folder: ' + $script:Root) }
    if ($script:Root -like "$env:ProgramFiles*" -or $script:Root -like "${env:ProgramFiles(x86)}*") { Add 'WARN' 'The package is inside Program Files, where normal users cannot write. Move it to a folder you own, for example C:\ERP-MES-Demo.' }

    foreach ($rel in @('app\ERP.MES.Desktop.exe', 'app\e_sqlite3.dll', 'app\appsettings.json', 'tools\provisioning\ERP.MES.Provisioning.exe', 'DEMO-ACCOUNTS.txt')) {
        $p = Join-Path $script:Root $rel
        if (Test-Path -LiteralPath $p) { Add 'OK' ('Found ' + $rel) } else { Add 'FAIL' ('Missing file ' + $rel + ' - extract the whole ZIP again (do not copy single files).') }
    }
    if (Test-Path -LiteralPath (Join-Path $script:Root 'app\appsettings.Local.json')) { Add 'WARN' 'app\appsettings.Local.json exists and overrides the shipped settings; delete it unless it is intentional.' }

    foreach ($dir in @($script:DataDir, $script:LogDir)) {
        try {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            $probe = Join-Path $dir ('.write-test-' + [guid]::NewGuid().ToString('N'))
            Set-Content -LiteralPath $probe -Value 'x'
            Remove-Item -LiteralPath $probe -Force
            Add 'OK' ('Writable: ' + $dir)
        } catch { Add 'FAIL' ('Cannot write to ' + $dir + ' (' + $_.Exception.Message + '). Move the package to a folder you own.') }
    }

    try {
        $drive = New-Object System.IO.DriveInfo ([System.IO.Path]::GetPathRoot($script:Root))
        $freeMb = [math]::Round($drive.AvailableFreeSpace / 1MB)
        if ($freeMb -lt 500) { Add 'FAIL' ("Only $freeMb MB free on $($drive.Name); at least 500 MB are needed.") } else { Add 'OK' ("Free disk space on $($drive.Name): $freeMb MB") }
    } catch { Add 'WARN' 'Could not read the free disk space.' }

    try { [void][TimeZoneInfo]::FindSystemTimeZoneById('Iran Standard Time'); Add 'OK' "Time zone 'Iran Standard Time' is available (the application needs it)." }
    catch { Add 'FAIL' "The Windows time zone 'Iran Standard Time' is not available on this computer; the application needs it for Jalali dates." }

    if (Test-Path -LiteralPath (Join-Path $env:WINDIR 'Fonts\tahoma.ttf')) { Add 'OK' 'Font Tahoma is installed (Persian text).' } else { Add 'WARN' 'Font Tahoma was not found; Persian text will use Segoe UI or Arial, which is readable but looks different.' }

    try {
        Add-Type -AssemblyName System.Windows.Forms
        $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $dpi = (Get-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -ErrorAction SilentlyContinue).AppliedDPI
        $scale = if ($dpi) { [math]::Round($dpi / 96 * 100) } else { $null }
        $res = ('{0}x{1}' -f $b.Width, $b.Height)
        if ($b.Width -lt 1280 -or $b.Height -lt 700) { Add 'WARN' ("Screen $res is small (the main window needs about 1100x680 DIP). Use a larger resolution or lower the display scaling.") }
        else { Add 'OK' ("Screen $res" + $(if ($scale) { ", display scaling about $scale %" } else { '' })) }
        if ($scale -and $scale -gt 150) { Add 'WARN' "Display scaling is $scale %; at the projector resolution check that nothing is cut off (125-150 % is the practical range)." }
    } catch { Add 'WARN' 'Could not read the screen resolution.' }

    $running = Get-Process -Name 'ERP.MES.Desktop' -ErrorAction SilentlyContinue
    if ($running) { Add 'WARN' 'The application is already running.' }
    return $findings
}

function Show-Preflight($Findings) {
    foreach ($f in $Findings) { Out-Step $f.Level $f.Text }
    $fails = @($Findings | Where-Object { $_.Level -eq 'FAIL' }).Count
    $warns = @($Findings | Where-Object { $_.Level -eq 'WARN' }).Count
    return [pscustomobject]@{ Fails = $fails; Warns = $warns }
}

# Removes the "downloaded from the internet" mark that Windows puts on files extracted from a downloaded ZIP (otherwise SmartScreen may block the executables).
function Unblock-Package {
    try { Get-ChildItem -LiteralPath $script:Root -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue } catch { }
}

function Invoke-Tool([string[]] $Arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:Tool
    $psi.Arguments = ($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['ERPMES_DEMO_PASSPHRASE'] = (Get-DemoPassphrase)
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd()
    $err = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    if ($out) { $out.TrimEnd().Split("`n") | ForEach-Object { Out-Step 'INFO' $_.TrimEnd() } }
    if ($err) { $err.TrimEnd().Split("`n") | ForEach-Object { Out-Step 'FAIL' $_.TrimEnd() } }
    return $p.ExitCode
}

function New-OneTimeFilePath { return (Join-Path $env:TEMP ('erpmes-demo-' + [guid]::NewGuid().ToString('N') + '.credentials.txt')) }

# Creates the database with the fictional seed data when it is missing; refuses anything that is not a marked demonstration database.
function Initialize-DemoDatabase {
    if (Test-Path -LiteralPath $script:DbPath) {
        $state = (& $script:Tool 'demo-status' $script:DbPath | Out-String).Trim()
        if ($state -ne 'Demo') { throw "The file $($script:DbPath) is not a demonstration database (status: $state). Nothing was changed. Move it away or run Reset-Demo." }
        Out-Step 'OK' 'Local demonstration database found.'
        return
    }
    Out-Step 'INFO' 'Creating the local demonstration database (about 20-60 seconds, one time)...'
    $code = Invoke-Tool @('demo-init', $script:DbPath, '--credentials', (New-OneTimeFilePath), '--presentation')
    if ($code -ne 0) { throw "Creating the demonstration database failed (exit code $code). See the lines above and logs\launcher-*.log." }
    Out-Step 'OK' 'Demonstration database created.'
}

function Start-DemoApplication {
    $env:ERPMES_DATABASE = $script:DbPath
    $env:ERPMES_LOG_DIR = $script:LogDir
    Write-DemoLog ('starting ' + $script:Exe)
    return (Start-Process -FilePath $script:Exe -WorkingDirectory (Split-Path -Parent $script:Exe) -PassThru)
}
