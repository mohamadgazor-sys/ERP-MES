# UI INTERACTION test of the real WPF application (docs/UI_SMOKE.md section 5): real clicks and typing through Microsoft UI Automation on a fresh, disposable demonstration database.
# It exercises role-specific controls, a premature action, request approval by Management, persistence across an application restart, Drawing, and item-scoped contract decisions.
# It does not call any service directly. Usage: ui-interact.ps1 -ExePath <Desktop exe> -DatabasePath <demo db> -AccountsFile <DEMO-ACCOUNTS.txt>
param(
    [Parameter(Mandatory = $true)][string] $ExePath,
    [Parameter(Mandatory = $true)][string] $DatabasePath,
    [Parameter(Mandatory = $true)][string] $AccountsFile
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$failures = New-Object System.Collections.Generic.List[string]
$checks = New-Object System.Collections.Generic.List[string]
function Note([string] $text) { Write-Host $text }
function Fail([string] $text) { $script:failures.Add($text); Write-Host "FAIL: $text" }
function Check([bool] $condition, [string] $okText, [string] $badText) { if ($condition) { $script:checks.Add($okText); Write-Host "CHECK OK: $okText" } else { Fail $badText } }

$line = Get-Content -LiteralPath $AccountsFile -Encoding UTF8 | Where-Object { $_ -match '^PASSPHRASE:\s*(\S.*)$' } | Select-Object -First 1
$Pass = ($line -replace '^PASSPHRASE:\s*', '').Trim()
Write-Host "::add-mask::$Pass"
$env:ERPMES_DATABASE = $DatabasePath

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
        $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        # Crop to the application's window (not the whole runner desktop, which also shows the CI agent console).
        $rect = $screen
        $pidNow = $null
        if (Get-Variable -Name Process -Scope Script -ErrorAction SilentlyContinue) { if ($script:Process) { $pidNow = $script:Process.Id } }
        if (-not $pidNow -and (Get-Variable -Name ShotPid -Scope Script -ErrorAction SilentlyContinue)) { $pidNow = $script:ShotPid }
        if ($pidNow) {
            $w = $AE::RootElement.FindFirst($Scope::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pidNow)))
            if ($w) {
                $b = $w.Current.BoundingRectangle
                $x = [int][Math]::Max(0, $b.X); $y = [int][Math]::Max(0, $b.Y)
                $wd = [int][Math]::Min($screen.Width - $x, $b.Width); $ht = [int][Math]::Min($screen.Height - $y, $b.Height)
                if ($wd -gt 100 -and $ht -gt 100) { $rect = New-Object System.Drawing.Rectangle($x, $y, $wd, $ht) }
            }
        }
        $bitmap = New-Object System.Drawing.Bitmap($rect.Width, $rect.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($rect.Location, [System.Drawing.Point]::Empty, $rect.Size)
        $factor = [Math]::Min(1.0, 1100.0 / $rect.Width)
        $scaled = New-Object System.Drawing.Bitmap($bitmap, [int]($rect.Width * $factor), [int]($rect.Height * $factor))
        $dir = Join-Path $env:RUNNER_TEMP 'ui-smoke-screens'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' } | Select-Object -First 1
        $parameters = New-Object System.Drawing.Imaging.EncoderParameters(1)
        $parameters.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [long]60)
        $scaled.Save((Join-Path $dir "$name.jpg"), $codec, $parameters)
        $graphics.Dispose(); $bitmap.Dispose(); $scaled.Dispose()
    } catch { Note "(screenshot $name not taken: $($_.Exception.Message))" }
}


function Save-Shot([string] $name) { Save-Screenshot $name }

$script:Process = $null
$script:Main = $null

function Start-App {
    $script:Process = Start-Process -FilePath $ExePath -PassThru
    $w = Get-AppWindow $script:Process.Id
    if (-not $w) { throw 'the application window did not appear' }
    return $w
}
function Stop-App {
    if ($script:Process -and -not $script:Process.HasExited) { $script:Process.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2; if (-not $script:Process.HasExited) { $script:Process.Kill() } }
    $script:Process = $null
}

# Signs in on the sign-in window that is currently shown (new process or after sign-out); returns the main window.
function Sign-In([string] $user) {
    Note "--- sign in as $user"
    $deadline = (Get-Date).AddSeconds(30)
    $box = $null
    while ((Get-Date) -lt $deadline -and -not $box) {
        foreach ($w in $AE::RootElement.FindAll($Scope::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $script:Process.Id)))) {
            $f = $w.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'SignInUsername')))
            if ($f -and -not $f.Current.IsOffscreen) { $box = $f; $win = $w; break }
        }
        if (-not $box) { Start-Sleep -Milliseconds 400 }
    }
    if (-not $box) { throw "sign-in window not found for $user" }
    Set-Text $box $user
    Type-Secret (Find-ById $win 'SignInPassword') $Pass
    Click (Find-ByName $win 'ورود' ([System.Windows.Automation.ControlType]::Button))
    $deadline = (Get-Date).AddSeconds(30)
    $main = $null
    while ((Get-Date) -lt $deadline -and -not $main) {
        $c = Get-AppWindow $script:Process.Id 5
        if ($c -and (Find-ById $c 'NavigationList' 2)) { $main = $c } else { Start-Sleep -Milliseconds 500 }
    }
    if (-not $main) { throw "main window did not appear for $user" }
    $script:Main = $main
    return $main
}

function Sign-Out {
    $btn = Find-ByName $script:Main 'خروج از حساب' ([System.Windows.Automation.ControlType]::Button) 10
    if (-not $btn) { throw 'sign-out button not found' }
    Click $btn
    Start-Sleep -Seconds 2
}

function Get-NavRows {
    $nav = Find-ById $script:Main 'NavigationList'
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $rows = @()
    foreach ($i in $nav.FindAll($Scope::Children, $cond)) { $t = @(Read-Texts $i 1); if ($t.Count -gt 0) { $rows += [pscustomobject]@{ Title = [string]$t[0]; Element = $i } } }
    return $rows
}
function Open-Page([string] $title) {
    for ($round = 0; $round -lt 8; $round++) {
        $row = Get-NavRows | Where-Object { $_.Title -eq $title } | Select-Object -First 1
        if ($row) {
            Select-Item $row.Element
            Start-Sleep -Milliseconds 900
            for ($w = 0; $w -lt 10; $w++) { $h = $script:Main.FindAll($Scope::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $title)), (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))))); if ($h.Count -ge 2) { return }; Start-Sleep -Milliseconds 400 }
            return
        }
        $last = Get-NavRows | Select-Object -Last 1
        if ($last) { try { $last.Element.SetFocus() } catch { }; [System.Windows.Forms.SendKeys]::SendWait('{PGDN}'); Start-Sleep -Milliseconds 500 }
    }
    throw "page '$title' not found in the navigation"
}

# Grid helpers: a DataGrid row exposes its cells as descendants with the cell text as Name.
function Get-GridRows($root) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    $out = @()
    foreach ($r in $root.FindAll($Scope::Descendants, $cond)) {
        $texts = @(); foreach ($d in $r.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) { $n = $d.Current.Name; if ($n -and $texts -notcontains $n) { $texts += $n } }
        $out += [pscustomobject]@{ Element = $r; Texts = $texts; Joined = ($texts -join ' | ') }
    }
    return $out
}
function Select-GridRow($root, [string] $contains) {
    $row = Get-GridRows $root | Where-Object { $_.Joined -like "*$contains*" } | Select-Object -First 1
    if (-not $row) { throw "grid row containing '$contains' not found" }
    Select-Item $row.Element
    Start-Sleep -Milliseconds 800
    return $row
}
function Get-RowText($root, [string] $contains) { $r = Get-GridRows $root | Where-Object { $_.Joined -like "*$contains*" } | Select-Object -First 1; if ($r) { return $r.Joined } else { return '' } }
function Find-Btn([string] $name, [int] $seconds = 3) { return (Find-ByName $script:Main $name ([System.Windows.Automation.ControlType]::Button) $seconds) }
function Wait-Enabled([string] $name, [int] $seconds = 12) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { $b = Find-Btn $name 1; if ($b -and $b.Current.IsEnabled) { return $b }; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $deadline)
    return $b
}
function Screen-Texts([int] $n = 40) { return ((Read-Texts $script:Main $n) -join ' / ') }
function Wait-RowChange($root, [string] $contains, [string] $before, [int] $seconds = 15) {
    $deadline = (Get-Date).AddSeconds($seconds)
    do { Start-Sleep -Milliseconds 600; $now = Get-RowText $root $contains } while ($now -eq $before -and (Get-Date) -lt $deadline)
    return $now
}
function Type-Into($element, [string] $text) { Set-Text $element $text }

try {
    $R14 = 'DEMO-1405-014'
    $R15 = 'DEMO-1405-015'

    # ---- 1. Central: role-specific controls and a premature action ----
    Start-App | Out-Null
    Sign-In 'demo.central' | Out-Null
    Open-Page 'درخواست‌ها'
    $grid = Find-ById $script:Main 'RequestGrid'
    Check ((Get-GridRows $grid).Count -eq 2) 'Central sees the two seeded sample requests' ('expected 2 requests, saw ' + (Get-GridRows $grid).Count)
    Select-GridRow $grid $R15 | Out-Null
    $submit = Find-Btn 'ارسال به مدیریت'
    Check ($submit -and -not $submit.Current.IsEnabled) 'PREMATURE ACTION: "send to Management" is disabled for the request that has no PDF yet' 'the submit button is missing or enabled for a request without a PDF'
    Check (-not (Find-Btn 'تأیید' 2)) 'ROLE: Central has no "approve" button' 'Central can see an approve button'
    Select-GridRow $grid $R14 | Out-Null
    $statusBefore = Get-RowText $grid $R14
    Note "  $R14 before: $statusBefore"
    Save-Shot 'interact-1-central-requests'
    Sign-Out

    # ---- 2. Management approves ----
    Sign-In 'demo.management' | Out-Null
    Open-Page 'درخواست‌ها'
    $grid = Find-ById $script:Main 'RequestGrid'
    Select-GridRow $grid $R14 | Out-Null
    $approve = Wait-Enabled 'تأیید'
    Check ($approve -and $approve.Current.IsEnabled) 'ROLE: Management sees an enabled "approve" button on the request waiting for it' 'no enabled approve button for Management'
    if ($approve) { Click $approve }
    $statusAfter = Wait-RowChange $grid $R14 $statusBefore
    Note "  $R14 after: $statusAfter"
    Check ($statusAfter -ne $statusBefore -and $statusAfter -ne '') 'Management approval changed the request status in the grid' 'the status did not change after approval'
    Save-Shot 'interact-2-management-approved'
    Stop-App

    # ---- 3. Restart: persistence; Planning forwards to Drawing ----
    Start-App | Out-Null
    Sign-In 'demo.planning' | Out-Null
    Open-Page 'درخواست‌ها'
    $grid = Find-ById $script:Main 'RequestGrid'
    $afterRestart = Get-RowText $grid $R14
    Check ($afterRestart -eq $statusAfter) 'PERSISTENCE: after closing and reopening the application the approved status is still shown' "status after restart differs: '$afterRestart' vs '$statusAfter'"
    Select-GridRow $grid $R14 | Out-Null
    $forward = Wait-Enabled 'ارسال به نقشه‌کشی'
    Check ($forward -and $forward.Current.IsEnabled) 'ROLE: Planning can send the approved request to Drawing' 'no forward-to-Drawing button for Planning'
    if ($forward) { Click $forward }
    $afterForward = Wait-RowChange $grid $R14 $statusAfter
    Check ($afterForward -ne $statusAfter) 'Planning forwarding to Drawing changed the status' 'status unchanged after forwarding'
    Sign-Out

    # ---- 4. Drawing records the result ----
    Sign-In 'demo.drawing' | Out-Null
    Open-Page 'درخواست‌ها'
    $grid = Find-ById $script:Main 'RequestGrid'
    Select-GridRow $grid $R14 | Out-Null
    Check (-not (Find-Btn 'تأیید' 2)) 'ROLE: Drawing has no "approve request" button' 'Drawing can see the approve button'
    $combo = Find-ById $script:Main 'OutcomeBox' 5
    if (-not $combo) { Fail 'the Drawing outcome box is missing' } else {
        $exp = $null
        if ($combo.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref] $exp)) { $exp.Expand(); Start-Sleep -Milliseconds 600 }
        $opt = $combo.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))
        if ($opt) { Select-Item $opt } else { Fail 'no outcome options listed' }
        Start-Sleep -Milliseconds 400
        $record = Find-Btn 'ثبت نتیجهٔ نقشه‌کشی'
        $beforeDrawing = Get-RowText $grid $R14
        if ($record) { Click $record } else { Fail 'record-result button missing' }
        $afterDrawing = Wait-RowChange $grid $R14 $beforeDrawing
        Check ($afterDrawing -ne $beforeDrawing) 'Drawing recording the result (first option: fully accepted) changed the request status' "status unchanged after Drawing result; screen: $(Screen-Texts 25)"
    }
    Save-Shot 'interact-3-drawing'
    Sign-Out

    # ---- 5. Planning: item-scoped contract decisions ----
    Sign-In 'demo.planning' | Out-Null
    Open-Page 'قراردادها'
    $cgrid = $script:Main.FindAll($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataGrid)))[0]
    Select-GridRow $script:Main $R14 | Out-Null
    $note = Find-ById $script:Main 'NoteBox' 5
    if ($note) { Type-Into $note 'تصمیم برای آزمون رابط کاربری' }
    $req = Wait-Enabled 'قرارداد لازم است'
    Check ($req -and $req.Current.IsEnabled) 'Planning can decide that the request needs a contract' "request-level decision button unavailable; screen: $(Screen-Texts 30)"
    if ($req) { Click $req; Start-Sleep -Seconds 2 }
    $items = Get-GridRows $script:Main | Where-Object { $_.Joined -like '*3.10*' -or $_.Joined -like '*3.20*' }
    Check ($items.Count -eq 2) 'the item grid lists both items of the request' ("expected 2 item rows, saw " + $items.Count)
    $row310 = Select-GridRow $script:Main '3.10'
    $before310 = Get-RowText $script:Main '3.10'
    $row320 = Select-GridRow $script:Main '3.20'
    $before320 = Get-RowText $script:Main '3.20'
    if ($note) { Type-Into $note 'این آیتم قطعه بدون قرارداد است' }
    $notReq = Wait-Enabled 'این آیتم: قرارداد لازم نیست'
    Check ($notReq -and $notReq.Current.IsEnabled) 'ITEM-SCOPED: the "this item does not need the contract" button is enabled for item 3.20' 'item decision button unavailable'
    if ($notReq) { Click $notReq }
    $after320 = Wait-RowChange $script:Main '3.20' $before320
    $after310 = Get-RowText $script:Main '3.10'
    Note "  3.10: $before310  ->  $after310"
    Note "  3.20: $before320  ->  $after320"
    Check ($after320 -ne $before320) 'ITEM-SCOPED: item 3.20 now shows its own decision' 'item 3.20 did not change'
    Check ($after310 -eq $before310) 'ITEM-SCOPED: item 3.10 is unchanged (still follows the request) - one item decision does not alter the other' 'item 3.10 changed when only 3.20 was decided'
    Check ($after310 -ne $after320) 'ITEM-SCOPED: the two items of one request show different decisions' 'both items show the same decision'
    Save-Shot 'interact-4-contract-items'
    Sign-Out

    # ---- 6. Management cannot decide per-item ----
    Sign-In 'demo.management' | Out-Null
    Open-Page 'قراردادها'
    Select-GridRow $script:Main $R14 | Out-Null
    $mg = Find-Btn 'این آیتم: قرارداد لازم نیست' 3
    Check (-not $mg -or -not $mg.Current.IsEnabled) 'UNAUTHORIZED: Management cannot make the per-item contract decision (button absent or disabled)' 'Management has an enabled per-item decision button'
    Save-Shot 'interact-5-management-contracts'
    Sign-Out
}
catch {
    Fail ('script error: ' + $_.Exception.Message + ' | ' + $_.ScriptStackTrace)
    try { Write-Host ('screen texts: ' + (Screen-Texts 60)) } catch { }
    Save-Shot 'interact-error'
}
finally { Stop-App }

Write-Host ''
Write-Host ("INTERACTION CHECKS PASSED: {0}" -f $checks.Count)
$checks | ForEach-Object { Write-Host (' + ' + $_) }
if ($failures.Count -gt 0) { Write-Host ("INTERACTION FAILURES: {0}" -f $failures.Count); $failures | ForEach-Object { Write-Host (' - ' + $_) }; exit 1 }
Write-Host 'UI interaction test passed.'
exit 0
