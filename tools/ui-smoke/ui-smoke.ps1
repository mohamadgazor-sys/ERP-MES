# UI smoke test of the real WPF application on a Windows runner (docs/UI_SMOKE.md). It launches the published Desktop build against a throw-away
# demonstration database, signs in through the real sign-in window, replaces the one-time password, and walks the navigation with Microsoft UI Automation.
# It records what it saw (window titles, page titles, grid row counts) in the log. It does NOT click through the business workflow: that is covered by
# EndToEndDemoTests at the service level. Passwords are random one-time values read from a protected temp file, masked first, and never printed.
param(
    [Parameter(Mandatory = $true)][string] $ExePath,
    [Parameter(Mandatory = $true)][string] $DatabasePath,
    [string] $CredentialsPath,
    [string] $AccountsFile,              # offline package: DEMO-ACCOUNTS.txt with a "PASSPHRASE:" line; the accounts then have NO one-time password to replace
    [string[]] $SignInOnly = @(),        # accounts that are only signed in (main window and navigation must appear), without walking every page
    [switch] $SkipWalk                   # do not walk the three standard accounts (used after a reset, with -SignInOnly)
)

$ErrorActionPreference = 'Stop'
$SignInOnly = @($SignInOnly | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })   # -File passes one string; accept a comma list
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$Cond = [System.Windows.Automation.Condition]
$Prop = [System.Windows.Automation.AutomationElement]

$NewPassword = 'Ui-Smoke-Pass-Phrase-9'   # throw-away database on a CI runner only
$failures = New-Object System.Collections.Generic.List[string]
$visited = New-Object System.Collections.Generic.List[string]

function Note([string] $text) { Write-Host $text }
function Fail([string] $text) { $script:failures.Add($text); Write-Host "FAIL: $text" }

$script:Fixed = $false
function Get-Passwords([string] $file) {
    $map = @{}
    foreach ($line in (Get-Content -LiteralPath $file -Encoding UTF8)) {
        if ($line -like "*`t*") { $parts = $line -split "`t"; $map[$parts[0]] = $parts[1]; Write-Host "::add-mask::$($parts[1])" }
    }
    return $map
}

function Find-ById($parent, [string] $id, [int] $timeoutSeconds = 20) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        $found = $parent.FindFirst($Scope::Descendants, $condition)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Find-ByName($parent, [string] $name, $controlType = $null, [int] $timeoutSeconds = 20) {
    $conditions = @(New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name))
    if ($controlType) { $conditions += New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType) }
    $condition = if ($conditions.Count -gt 1) { New-Object System.Windows.Automation.AndCondition($conditions) } else { $conditions[0] }
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        $found = $parent.FindFirst($Scope::Descendants, $condition)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Get-AppWindow([int] $processId, [int] $timeoutSeconds = 30) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        $windows = $AE::RootElement.FindAll($Scope::Children, $condition)
        if ($windows.Count -gt 0) { return $windows[0] }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Set-Text($element, [string] $text) {
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $pattern)) { $pattern.SetValue($text); return }
    throw 'The element does not support ValuePattern.'
}

# A WPF PasswordBox has no ValuePattern: focus it and type. SendKeys treats + ^ % ~ ( ) { } [ ] specially, so they are wrapped in braces.
function Type-Secret($element, [string] $secret) {
    $element.SetFocus()
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait(([regex]::Replace($secret, '[+^%~(){}\[\]]', '{$0}')))
}

function Click($element) {
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) { $pattern.Invoke(); return }
    throw 'The element does not support InvokePattern.'
}

function Select-Item($element) {
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref] $pattern)) { $pattern.Select(); return }
    throw 'The element does not support SelectionItemPattern.'
}

function Read-Texts($parent, [int] $limit = 25) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $all = $parent.FindAll($Scope::Descendants, $condition)
    $names = @()
    for ($i = 0; $i -lt [Math]::Min($all.Count, $limit); $i++) { $n = $all[$i].Current.Name; if ($n) { $names += $n } }
    return $names
}

function Count-Type($parent, $controlType) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)
    return $parent.FindAll($Scope::Descendants, $condition).Count
}

function Save-Screenshot([string] $name) {
    try {
        Add-Type -AssemblyName System.Drawing
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bitmap = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $factor = [Math]::Min(1.0, 1100.0 / $bounds.Width)
        $scaled = New-Object System.Drawing.Bitmap($bitmap, [int]($bounds.Width * $factor), [int]($bounds.Height * $factor))
        $dir = Join-Path $env:RUNNER_TEMP 'ui-smoke-screens'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' } | Select-Object -First 1
        $parameters = New-Object System.Drawing.Imaging.EncoderParameters(1)
        $parameters.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]55)
        $scaled.Save((Join-Path $dir "$name.jpg"), $codec, $parameters)
        $graphics.Dispose(); $bitmap.Dispose(); $scaled.Dispose()
    } catch { Note "(screenshot $name not taken: $($_.Exception.Message))" }
}

# Evidence for the offline claim: the application process must hold no network connection (loopback excluded).
function Report-Connections($process) {
    try {
        $tcp = @(Get-NetTCPConnection -OwningProcess $process.Id -ErrorAction SilentlyContinue | Where-Object { $_.RemoteAddress -notin @('127.0.0.1', '::1', '0.0.0.0', '::') })
        $udp = @(Get-NetUDPEndpoint -OwningProcess $process.Id -ErrorAction SilentlyContinue)
        Note ("NETWORK: process {0}: {1} non-loopback TCP connection(s), {2} UDP endpoint(s)" -f $process.Id, $tcp.Count, $udp.Count)
        if ($tcp.Count -gt 0) { Fail ("the application holds " + $tcp.Count + " non-loopback TCP connection(s): " + (($tcp | ForEach-Object { $_.RemoteAddress + ':' + $_.RemotePort }) -join ', ')) }
    } catch { Note "(connection check not available: $($_.Exception.Message))" }
}

function Invoke-Session([string] $username, [string] $oneTimePassword, [string[]] $mustSee, [switch] $QuickOnly) {
    Note "=== Session: $username ==="
    $env:ERPMES_DATABASE = $DatabasePath
    $process = Start-Process -FilePath $ExePath -PassThru
    try {
        $window = Get-AppWindow $process.Id
        if (-not $window) { Fail "$username`: no window appeared"; return }
        Note "First window: '$($window.Current.Name)'"
        $user = Find-ById $window 'SignInUsername'
        $pass = Find-ById $window 'SignInPassword'
        if (-not $user -or -not $pass) { Fail "$username`: the sign-in controls were not found"; Save-Screenshot "$username-signin"; return }
        Save-Screenshot "$username-1-signin"
        Set-Text $user $username
        Type-Secret $pass $oneTimePassword
        $signInButton = Find-ByName $window 'ورود' ([System.Windows.Automation.ControlType]::Button)
        if (-not $signInButton) { Fail "$username`: the sign-in button was not found"; return }
        Click $signInButton

        # The demonstration accounts must replace their one-time password first (not in the offline package, where the launcher already did it).
        $current = Find-ById $window 'CurrentPassword' $(if ($script:Fixed) { 4 } else { 15 })
        if ($script:Fixed) {
            if ($current -and -not $current.Current.IsOffscreen) { Fail "$username`: the change-password panel appeared although the offline package should have replaced the one-time password" }
            else { Note 'No change-password panel (the launcher already replaced the one-time password), as expected.' }
        } elseif ($current -and -not $current.Current.IsOffscreen) {
            Note 'The change-password panel is shown (one-time password), as required.'
            Type-Secret $current $oneTimePassword
            Type-Secret (Find-ById $window 'NewPassword') $NewPassword
            Type-Secret (Find-ById $window 'NewPasswordRepeat') $NewPassword
            Save-Screenshot "$username-2-change"
            Click (Find-ByName $window 'تغییر گذرواژه' ([System.Windows.Automation.ControlType]::Button))
        } else { Fail "$username`: the change-password panel did not appear for a one-time password" }

        $main = $null
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline -and -not $main) {
            $candidate = Get-AppWindow $process.Id 5
            if ($candidate -and (Find-ById $candidate 'NavigationList' 2)) { $main = $candidate } else { Start-Sleep -Milliseconds 500 }
        }
        if (-not $main) { Fail "$username`: the main window with the navigation list did not appear"; Save-Screenshot "$username-3-nomain"; return }
        Note "Main window: '$($main.Current.Name)'"
        $nav = Find-ById $main 'NavigationList'
        if ($QuickOnly) {
            $count = $nav.FindAll($Scope::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))).Count
            Note "Signed in; navigation shows $count entries."
            if ($count -lt 1) { Fail "$username`: no navigation entries after sign-in" }
            Save-Screenshot "$username-main"
            Report-Connections $process
            return $main
        }
        $listItemCondition = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
        # The list is virtualised: only the realised rows exist in the UI Automation tree, so the walk scrolls with PageDown until no new row appears.
        # A row's own Name is the view-model's ToString(); the visible title is its Text child.
        function Get-Rows { $r = @(); foreach ($i in $nav.FindAll($Scope::Children, $listItemCondition)) { $t = @(Read-Texts $i 1); if ($t.Count -gt 0) { $r += [pscustomobject]@{ Title = [string]$t[0]; Element = $i } } }; return $r }
        $seen = New-Object System.Collections.Generic.List[string]
        for ($round = 0; $round -lt 8; $round++) {
            $fresh = @(Get-Rows | Where-Object { $seen -notcontains $_.Title })
            if ($fresh.Count -eq 0) { break }
            foreach ($row in $fresh) {
                $title = $row.Title
                $seen.Add($title)
                Select-Item $row.Element
                Start-Sleep -Milliseconds 700
                $headingCondition = New-Object System.Windows.Automation.AndCondition(
                    (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $title)),
                    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
                $headings = $main.FindAll($Scope::Descendants, $headingCondition)
                for ($wait = 0; $headings.Count -lt 2 -and $wait -lt 8; $wait++) { Start-Sleep -Milliseconds 500; $headings = $main.FindAll($Scope::Descendants, $headingCondition) }
                if ($headings.Count -lt 2) { Note ("  heading not found; texts on screen: {0}" -f ((Read-Texts $main 60) -join ' / ')) }
                $grids = Count-Type $main ([System.Windows.Automation.ControlType]::DataGrid)
                $buttons = Count-Type $main ([System.Windows.Automation.ControlType]::Button)
                $rows = Count-Type $main ([System.Windows.Automation.ControlType]::DataItem)
                # One match is the navigation entry itself; a second is the page heading.
                Note ("PAGE '{0}': heading={1} datagrids={2} rows={3} buttons={4}" -f $title, ($headings.Count -ge 2), $grids, $rows, $buttons)
                if ($headings.Count -lt 2) { Fail "$username`: opening '$title' did not show its heading" }
                # What the seeded database must show on these pages (docs/DEMO_SEED_AND_RESET.md): 3 fictional customers, 2 sample requests.
                if ($title -eq 'مشتریان' -and $rows -lt 3) { Fail "$username`: the customers grid shows $rows rows, expected at least 3" }
                if ($title -eq 'درخواست‌ها' -and $rows -lt 2) { Fail "$username`: the requests grid shows $rows rows, expected at least 2" }
                if ($title -eq 'نوبت‌های کاری' -and $grids -lt 1) { Fail "$username`: the work board shows no grid" }
                $visited.Add("$username/$title")
                Save-Screenshot ("{0}-{1}" -f $username, $visited.Count)
            }
            $row0 = Get-Rows | Select-Object -Last 1
            if ($row0) { try { $row0.Element.SetFocus() } catch { }; [System.Windows.Forms.SendKeys]::SendWait('{PGDN}'); Start-Sleep -Milliseconds 500 }
        }
        Note "Navigation ($($seen.Count)): $($seen -join ' | ')"
        foreach ($required in $mustSee) { if ($seen -notcontains $required) { Fail "$username`: the navigation lacks '$required'" } }
        Report-Connections $process
        return $main
    } finally {
        if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $process.HasExited) { $process.Kill() } }
    }
}

if ($AccountsFile) {
    $script:Fixed = $true
    $line = Get-Content -LiteralPath $AccountsFile -Encoding UTF8 | Where-Object { $_ -match '^PASSPHRASE:\s*(\S.*)$' } | Select-Object -First 1
    if (-not $line) { throw 'No PASSPHRASE line in the accounts file.' }
    $pass = ($line -replace '^PASSPHRASE:\s*', '').Trim()
    Write-Host "::add-mask::$pass"
    $passwords = @{}
    foreach ($u in @('demo.admin', 'demo.central', 'demo.management', 'demo.planning', 'demo.drawing', 'demo.qc', 'demo.warehouse', 'demo.production', 'demo.supervisor')) { $passwords[$u] = $pass }
} else {
    $passwords = Get-Passwords $CredentialsPath
}
Note "Accounts available: $($passwords.Keys.Count)"
if ($passwords.Keys.Count -ne 9) { Fail "expected nine accounts, found $($passwords.Keys.Count)" }

if (-not $SkipWalk) {
    Invoke-Session 'demo.supervisor' $passwords['demo.supervisor'] @('نوبت‌های کاری') | Out-Null
    Invoke-Session 'demo.planning' $passwords['demo.planning'] @('صف واحدهای تولید', 'قراردادها', 'تعریف آیتم‌ها و مرحله‌ها') | Out-Null
    Invoke-Session 'demo.central' $passwords['demo.central'] @('درخواست‌ها') | Out-Null
}
foreach ($u in $SignInOnly) { Invoke-Session $u $passwords[$u] @() -QuickOnly | Out-Null }

Note "Visited pages: $($visited.Count)"
if ($failures.Count -gt 0) { Note "FAILURES: $($failures.Count)"; $failures | ForEach-Object { Note " - $_" }; exit 1 }
Note 'UI smoke test passed: every listed page opened with its heading.'
exit 0
