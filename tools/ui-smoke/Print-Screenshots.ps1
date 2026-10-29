# Prints chosen screenshots of the UI run into the job log as base64 lines (SHOT|name|base64) so that they can be decoded from the log. They show only fictional demo data;
# password fields are masked by the application. Usage: Print-Screenshots.ps1 -Names a,b,c
param([string[]] $Names)
$dir = Join-Path $env:RUNNER_TEMP 'ui-smoke-screens'
if (-not (Test-Path -LiteralPath $dir)) { Write-Host 'No screenshots were taken.'; exit 0 }
$files = Get-ChildItem -LiteralPath $dir -Filter '*.jpg' | Sort-Object Name
Write-Host ('Screenshots taken: ' + (($files | ForEach-Object { $_.BaseName + ' (' + [math]::Round($_.Length / 1KB) + ' KB)' }) -join ', '))
foreach ($n in $Names) {
    $f = $files | Where-Object { $_.BaseName -eq $n } | Select-Object -First 1
    if ($f) { Write-Host ('SHOT|' + $n + '|' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($f.FullName))) }
}
