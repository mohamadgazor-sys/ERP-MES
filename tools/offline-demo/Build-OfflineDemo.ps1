# Builds the offline demo package (ZIP + SHA-256) from this repository. Run on Windows with the .NET SDK pinned in global.json:
#   pwsh tools\offline-demo\Build-OfflineDemo.ps1 -Version 2026.10.29-abc1234 -OutDir out
# The package is a plain folder deployment: a self-contained win-x64 publish of the Desktop application (the .NET runtime is bundled), a self-contained publish of the provisioning tool,
# the launcher scripts, the demo account list, the guide and a manifest. The SQL Server provider is NOT part of it (and not referenced by the Desktop project).
param(
    [Parameter(Mandatory = $true)][string] $Version,
    [string] $OutDir = (Join-Path (Get-Location) 'out')
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$name = "ERP-MES-OfflineDemo-$Version-win-x64"
$stage = Join-Path $OutDir $name
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Run([string] $What, [scriptblock] $Block) { Write-Host "== $What"; & $Block; if ($LASTEXITCODE -ne 0) { throw "$What failed with exit code $LASTEXITCODE" } }

Push-Location $repo
try {
    Run 'publish Desktop (self-contained, win-x64, Release)' { dotnet publish src/ERP.MES.Desktop/ERP.MES.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false --output (Join-Path $stage 'app') }
    Run 'publish Provisioning (self-contained, win-x64, Release)' { dotnet publish src/ERP.MES.Provisioning/ERP.MES.Provisioning.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false --output (Join-Path $stage 'tools\provisioning') }
} finally { Pop-Location }

# Launcher, scripts, accounts, guide.
Copy-Item -Path (Join-Path $PSScriptRoot 'package\*') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repo 'docs\OFFLINE_DEMO_GUIDE.md') -Destination (Join-Path $stage 'OFFLINE_DEMO_GUIDE.md') -Force
Copy-Item -LiteralPath (Join-Path $repo 'docs\DEMO_WALKTHROUGH.md') -Destination (Join-Path $stage 'DEMO_WALKTHROUGH.md') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'data'), (Join-Path $stage 'logs') | Out-Null
Set-Content -LiteralPath (Join-Path $stage 'data\README.txt') -Value 'The local demo database (erpmes.demo.db) is created here by Start-Demo or Reset-Demo. Only that file is ever replaced by a reset.'
Set-Content -LiteralPath (Join-Path $stage 'logs\README.txt') -Value 'Launcher and application logs. They never contain passwords. Collect-Diagnostics packs them (without the database) for support.'

# Must not ship: anything that could carry a secret, a developer file or the production provider.
$forbidden = Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -match '(?i)appsettings\.Local\.json|\.pdb$|\.credentials\.txt$|SqlServer|SqlClient|\.db$|\.db-wal$|\.db-shm$' }
if ($forbidden) { throw ('Forbidden files in the package: ' + (($forbidden | ForEach-Object Name) -join ', ')) }

# ---- Manifest (components, versions, architecture, bundled or supplied by Windows) ----
$runtimeConfig = Get-Content -LiteralPath (Join-Path $stage 'app\ERP.MES.Desktop.runtimeconfig.json') -Raw | ConvertFrom-Json
$frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks | ForEach-Object { "{0} {1}" -f $_.name, $_.version })
$deps = Get-Content -LiteralPath (Join-Path $stage 'app\ERP.MES.Desktop.deps.json') -Raw | ConvertFrom-Json
$packages = @($deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' -and $_.Name -notmatch '^(runtimepack|Microsoft\.(NETCore|WindowsDesktop)\.App)' } | ForEach-Object { $_.Name.Replace('/', ' ') } | Sort-Object)
$m = New-Object System.Collections.Generic.List[string]
$m.Add("ERP/MES offline demo package - MANIFEST")
$m.Add("Package: $name")
$m.Add("Built (UTC): " + (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm'))
$m.Add("Source commit: " + $(if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (git -C $repo rev-parse HEAD) }))
$m.Add("Target: Windows x64 (win-x64). Windows 11 24H2 (build 26100) or newer is the recommended and officially supported target; see OFFLINE_DEMO_GUIDE.md section 1.")
$m.Add("")
$m.Add("BUNDLED (inside this folder; nothing to install):")
foreach ($f in $frameworks) { $m.Add("  .NET runtime framework  $f   win-x64, self-contained (includes the WPF desktop libraries)") }
foreach ($p in $packages) { $m.Add("  NuGet package           $p   (managed library)") }
$sqlite = Get-ChildItem -LiteralPath (Join-Path $stage 'app') -Filter 'e_sqlite3.dll' -Recurse | Select-Object -First 1
$m.Add("  Native SQLite           e_sqlite3.dll ($([math]::Round($sqlite.Length / 1KB)) KB), x64, from SQLitePCLRaw.lib.e_sqlite3")
$m.Add("  Application             ERP.MES.Desktop.exe (WPF, MVVM), ERP.MES.Provisioning.exe (demo-database tool used only by the launcher)")
$m.Add("")
$m.Add("SUPPLIED BY WINDOWS (not in the package; present on every supported Windows 11):")
$m.Add("  Windows PowerShell 5.1 (launcher scripts), Universal C Runtime, Tahoma font (falls back to Segoe UI / Arial), time-zone database ('Iran Standard Time'), GDI/DirectX/WPF native graphics stack")
$m.Add("")
$m.Add("NOT REQUIRED: internet, NuGet, a separate .NET Desktop Runtime, SQL Server, WebView2, Visual C++ redistributable (see OFFLINE_DEMO_GUIDE.md section 8), any CNC/hardware driver, an administrator account.")
$m.Add("The production SQL Server provider and its packages are not part of this package; the application is configured for the local SQLite file only.")
Set-Content -LiteralPath (Join-Path $stage 'MANIFEST.txt') -Value $m -Encoding UTF8

# ---- File list with SHA-256 ----
$root = (Resolve-Path $stage).Path
$lines = Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -ne 'FILES.sha256' } | Sort-Object FullName | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($root.Length + 1).Replace('\', '/')
}
Set-Content -LiteralPath (Join-Path $stage 'FILES.sha256') -Value $lines -Encoding ASCII

# ---- ZIP + checksum ----
$zip = Join-Path $OutDir "$name.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zip.sha256" -Value ("{0}  {1}" -f $hash, (Split-Path -Leaf $zip)) -Encoding ASCII
Write-Host ("PACKAGE: {0}  {1:N1} MB  SHA256 {2}" -f $zip, ((Get-Item $zip).Length / 1MB), $hash)
