param(
  [Parameter(Mandatory)][ValidatePattern('^[A-H]$')][string]$Test,
  # Holds installers-4.0.0-rc.1, installers-4.0.0-rc.2, old-3.4.0-rc.2 and old-3.4.0.
  [Parameter(Mandatory)][string]$Kit,
  [Parameter(Mandatory)][string]$Out
)
# Phase 0 test of the 4.0.0 Windows setup (docs/maintainer/V4InstallerAndCleanupPlan.md, tests A to H).
# Installs on the machine it runs on, so run it only on a throwaway machine such as a CI runner.
# Drives the setup windows with UI Automation and writes a log, result.json and screenshots to $Out.
# Exits with 1 when a check fails. Windows PowerShell 5.1. Temporary, goes with v4-installer-trial.yml.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$kit = $Kit
$out = Join-Path $Out $Test
New-Item -ItemType Directory -Force $out | Out-Null

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
. (Join-Path $here 'Aumid.ps1')

$script:log = Join-Path $out 'log.txt'
$script:shot = 0
$script:result = [ordered]@{ Test = $Test; Checks = @(); Notes = @(); Questions = @() }

function Log([string]$m) { $line = '{0:HH:mm:ss} {1}' -f (Get-Date), $m; Add-Content -Path $script:log -Value $line; Write-Host $line }
function Note([string]$m) { Log "NOTE $m"; $script:result.Notes += $m }
function Check([string]$name, [bool]$ok, [string]$detail = '') {
  Log ("CHECK {0} {1} {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $detail)
  $script:result.Checks += [ordered]@{ Name = $name; Pass = $ok; Detail = $detail }
}
function Shot([string]$name) {
  $script:shot++
  $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
  $file = Join-Path $out ('{0:D2}-{1}.png' -f $script:shot, $name)
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

# ---------- UI Automation ----------
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$TrueCondition = [System.Windows.Automation.Condition]::TrueCondition

function Get-TopWindows { @($AE::RootElement.FindAll($Scope::Children, $TrueCondition)) }
function Get-Descendants($el) { @($el.FindAll($Scope::Descendants, $TrueCondition)) }
function Get-Texts($el) { (Get-Descendants $el | ForEach-Object { $_.Current.Name }) -join ' | ' }
function Find-Id($el, [string]$id) {
  $el.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}
function Find-Name($el, [string]$like) { Get-Descendants $el | Where-Object { $_.Current.Name -like $like } | Select-Object -First 1 }
function Find-Class($el, [string]$class) { Get-Descendants $el | Where-Object { $_.Current.ClassName -eq $class } }

# UI Automation finds the controls, but without the Win32 proxies it offers no patterns for the classic
# controls of NSIS. Window messages act on them instead.
if (-not ('Win32Control' -as [type])) {
  Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class Win32Control {
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
"@
}
function Get-Hwnd($el) { [IntPtr]$el.Current.NativeWindowHandle }
# Tells the dialog that the control was clicked. Posted, so that a message box it opens cannot block us.
function Invoke-Control($el) {
  $h = Get-Hwnd $el
  $id = [Win32Control]::GetDlgCtrlID($h)
  [void][Win32Control]::PostMessage([Win32Control]::GetParent($h), 0x0111, [IntPtr]($id -band 0xFFFF), $h)
}
function Get-Toggle($el) { $(if ([Win32Control]::SendMessage((Get-Hwnd $el), 0x00F0, [IntPtr]::Zero, [IntPtr]::Zero) -eq [IntPtr]1) { 'On' } else { 'Off' }) }
function Set-Toggle($el, [bool]$on) {
  [void][Win32Control]::SendMessage((Get-Hwnd $el), 0x00F1, [IntPtr]$(if ($on) { 1 } else { 0 }), [IntPtr]::Zero)
  Invoke-Control $el
}
# Checks one radio button and clears the others in the same dialog.
function Select-Radio($el, $siblings) {
  foreach ($s in $siblings) { if ($s.Current.NativeWindowHandle -ne $el.Current.NativeWindowHandle) { [void][Win32Control]::SendMessage((Get-Hwnd $s), 0x00F1, [IntPtr]::Zero, [IntPtr]::Zero) } }
  Set-Toggle $el $true
}
# A real left click in the middle of the element, for the taskbar, which ignores the Invoke pattern.
function Invoke-Click($el) {
  $r = $el.Current.BoundingRectangle
  [void][Win32Control]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
  [Win32Control]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Milliseconds 100
  [Win32Control]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}
function Get-WindowText($el) {
  $sb = New-Object System.Text.StringBuilder 1024
  [void][Win32Control]::SendMessage((Get-Hwnd $el), 0x000D, [IntPtr]1024, $sb)
  $sb.ToString()
}

# NSIS windows are dialogs (#32770). The wizard has the Back button (3), a message box has not. On the
# first page the button is hidden, so UI Automation does not list it; GetDlgItem finds it anyway.
function Get-NsisWindows {
  Get-TopWindows | Where-Object { $_.Current.ClassName -eq '#32770' -and $_.Current.Name -like '*ARCitISA*' -and -not $_.Current.IsOffscreen }
}
function Test-Wizard($w) { [Win32Control]::GetDlgItem((Get-Hwnd $w), 3) -ne [IntPtr]::Zero }

# Answers a message box: Yes (6), No (7) or the only button. Returns its text.
function Send-MessageBoxAnswer($w, [string]$answer) {
  $text = (Find-Class $w 'Static' | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join ' '
  Shot 'message-box'
  $button = $(switch ($answer) { 'Yes' { Find-Id $w '6' } 'No' { Find-Id $w '7' } default { $null } })
  if ($null -eq $button) { $button = Find-Class $w 'Button' | Select-Object -First 1 }
  Log "message box: '$text' -> $answer ($($button.Current.Name))"
  $script:result.Questions += $text
  Invoke-Control $button
  $text
}

function Wait-Until([scriptblock]$cond, [int]$seconds, [string]$what) {
  $deadline = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $deadline) { $r = & $cond; if ($r) { return $r }; Start-Sleep -Milliseconds 500 }
  Log "timeout after $seconds s waiting for $what"
  $null
}

# ---------- Machine state ----------
$shell = New-Object -ComObject WScript.Shell
function Get-Links {
  $roots = @(
    "$env:APPDATA\Microsoft\Windows\Start Menu\Programs",
    "$env:ProgramData\Microsoft\Windows\Start Menu\Programs",
    [Environment]::GetFolderPath('Desktop'),
    "$env:PUBLIC\Desktop")
  foreach ($r in $roots) {
    if (Test-Path $r) {
      Get-ChildItem -Recurse -Filter *.lnk $r -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ARCitISA|swate|arcitisa' } | ForEach-Object {
        [ordered]@{ Path = $_.FullName; Target = $shell.CreateShortcut($_.FullName).TargetPath; Aumid = [AumidReader]::ForFile($_.FullName) }
      }
    }
  }
}
function Get-Entries {
  foreach ($hive in 'HKCU', 'HKLM') {
    $k = "${hive}:\Software\Microsoft\Windows\CurrentVersion\Uninstall"
    Get-ChildItem $k -ErrorAction SilentlyContinue | ForEach-Object {
      $p = Get-ItemProperty $_.PSPath
      if ($p.DisplayName -like '*ARCitISA*') { [ordered]@{ Hive = $hive; Key = $_.PSChildName; Name = $p.DisplayName; Version = $p.DisplayVersion; Location = $p.InstallLocation } }
    }
  }
}
function Get-State([string]$label) {
  $s = [ordered]@{
    Label = $label
    Entries = @(Get-Entries)
    SquirrelSwate = @(if (Test-Path "$env:LOCALAPPDATA\swate") { Get-ChildItem -Force "$env:LOCALAPPDATA\swate" | ForEach-Object Name })
    SquirrelArcitisa = @(if (Test-Path "$env:LOCALAPPDATA\arcitisa") { Get-ChildItem -Force "$env:LOCALAPPDATA\arcitisa" | ForEach-Object Name })
    PerUserApp = Test-Path "$env:LOCALAPPDATA\Programs\ARCitISA\ARCitISA.exe"
    AllUsersApp = Test-Path "$env:ProgramFiles\ARCitISA\ARCitISA.exe"
    Links = @(Get-Links)
    AppData = @(Get-ChildItem -Force $env:APPDATA -Directory | Where-Object { $_.Name -match 'swate|arcitisa' } | ForEach-Object { '{0} (created {1:HH:mm:ss}, entries: {2})' -f $_.Name, $_.CreationTime, ((Get-ChildItem -Force $_.FullName | ForEach-Object Name) -join ',') })
    StartApps = @(try { Get-StartApps | Where-Object { $_.Name -like '*ARCitISA*' } | ForEach-Object { '{0} = {1}' -f $_.Name, $_.AppID } } catch { "Get-StartApps failed: $($_.Exception.Message)" })
    # What keeps an old app-<version> folder: its remaining files and the processes started from it.
    OldAppFiles = @(foreach ($id in 'swate', 'arcitisa') { Get-ChildItem -Force -Recurse -File "$env:LOCALAPPDATA\$id\app-*" -ErrorAction SilentlyContinue | Select-Object -First 20 | ForEach-Object { $_.FullName.Substring($env:LOCALAPPDATA.Length + 1) } })
    OldAppProcesses = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like "$env:LOCALAPPDATA\swate\*" -or $_.ExecutablePath -like "$env:LOCALAPPDATA\arcitisa\*" } | ForEach-Object { '{0} {1}' -f $_.ProcessId, $_.ExecutablePath })
  }
  Log ("STATE {0}: {1}" -f $label, ($s | ConvertTo-Json -Depth 5 -Compress))
  $s
}
function Test-AppFolder([string]$dir) { Test-Path (Join-Path $dir 'app-*') }
function Test-Entry($state, [string]$key) { @($state.Entries | Where-Object { $_.Key -eq $key }).Count -gt 0 }
function Test-Link($state, [string]$like) { @($state.Links | Where-Object { $_.Path -like $like }).Count -gt 0 }

# ---------- Actions ----------
$guid = '716c9faf-756d-4e23-94c8-6d9e8d665a44'
$appId = 'org.nfdi4plants.app-arc-management-core'
$rc1 = Join-Path $kit 'installers-4.0.0-rc.1\ARCitISA-Setup-4.0.0-rc.1-x64.exe'
$rc2 = Join-Path $kit 'installers-4.0.0-rc.2\ARCitISA-Setup-4.0.0-rc.2-x64.exe'

function Install-Squirrel([string]$setup, [string]$id, [string]$exe, [bool]$leaveRunning = $false) {
  Log "installing $setup"
  Start-Process $setup | Out-Null
  $null = Wait-Until { (Test-AppFolder "$env:LOCALAPPDATA\$id") -and (Test-Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$id") } 240 "Squirrel install of $id"
  $null = Wait-Until { Get-Process $exe -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } } 120 "window of $exe"
  Start-Sleep -Seconds 10
  Shot "squirrel-$id-running"
  if (-not $leaveRunning) { Close-App $exe }
}
function Close-App([string]$exe) {
  Get-Process $exe -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | ForEach-Object { [void]$_.CloseMainWindow() }
  $null = Wait-Until { -not (Get-Process $exe -ErrorAction SilentlyContinue) } 30 "$exe to exit"
  Get-Process $exe -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 2
}

# Opens a file the way a user does. With UAC on and this script elevated, Explorer starts it, so it
# does not inherit the admin rights of this script.
function Open-AsUser([string]$path) {
  Log "opening $path"
  if ($script:openViaExplorer) { Start-Process explorer.exe -ArgumentList "`"$path`"" } else { Start-Process $path }
  Start-Sleep -Seconds 2
}
function Start-Setup([string]$path) { Open-AsUser $path }
function Get-SetupProcesses([string]$path) { @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $path }) }

# Drives the wizard until it closes. Options: Question (Yes/No/None), Mode (user/all), Desktop, Run.
function Invoke-Wizard([string]$path, [hashtable]$o) {
  $r = [ordered]@{ Pages = @(); Questions = 0; Desktop = $null; Folder = $null; Errors = @(); Closed = $false }
  $deadline = (Get-Date).AddMinutes(10)
  $last = ''
  $seen = 0
  while ((Get-Date) -lt $deadline) {
    $windows = @(Get-NsisWindows)
    if ($windows.Count -eq 0) {
      if ($seen -gt 0 -and (Get-SetupProcesses $path).Count -eq 0) { $r.Closed = $true; break }
      if ($seen -eq 0 -and ((Get-Date) - $deadline.AddMinutes(-10)).TotalSeconds -gt 60) { Log 'no setup window within 60 s'; break }
      Start-Sleep -Milliseconds 500; continue
    }
    $seen++
    $w = Select-NsisWindow $windows
    if (-not (Test-Wizard $w)) {
      $text = (Get-Texts $w)
      if ($text -like '*An older version of ARCitISA was found*') {
        $r.Questions++
        Send-MessageBoxAnswer $w $o.Question | Out-Null
      } else {
        $r.Errors += (Send-MessageBoxAnswer $w 'OK')
      }
      Start-Sleep -Seconds 1; continue
    }
    $texts = Get-Texts $w
    $page = $(if ($texts -like '*Anyone who uses this computer*') { 'mode' }
      elseif ($texts -like '*Create a desktop shortcut*') { 'shortcuts' }
      elseif ($texts -like '*Destination Folder*') { 'folder' }
      elseif ($texts -like '*Completing*') { 'finish' }
      elseif ($texts -like '*Welcome*') { 'welcome' }
      else { 'progress' })
    $next = Find-Id $w '1'
    if ($page -ne $last) { Shot "page-$page"; $r.Pages += $page; Log "page $page"; $last = $page }
    if ($null -eq $next -or -not $next.Current.IsEnabled) { Start-Sleep -Milliseconds 500; continue }
    switch ($page) {
      'mode' {
        $allUsers = Find-Name $w 'Anyone who uses this computer*'
        $onlyMe = Find-Name $w 'Only for me*'
        Select-Radio $(if ($o.Mode -eq 'all') { $allUsers } else { $onlyMe }) @($allUsers, $onlyMe); Start-Sleep -Milliseconds 300
        Shot 'page-mode-set'
      }
      'folder' {
        $edit = Find-Class $w 'Edit' | Select-Object -First 1
        if ($edit) { $r.Folder = Get-WindowText $edit; Log "folder $($r.Folder)" }
      }
      'shortcuts' {
        $box = Find-Name $w 'Create a desktop shortcut'
        $r.Desktop = Get-Toggle $box
        Log "desktop checkbox initially $($r.Desktop)"
        Set-Toggle $box ([bool]$o.Desktop); Start-Sleep -Milliseconds 300
        Shot 'page-shortcuts-set'
      }
      'finish' {
        $run = Find-Name $w 'Run *'
        if ($run) { Set-Toggle $run ([bool]$o.Run) }
      }
      'progress' { Start-Sleep -Milliseconds 500; continue }
    }
    Invoke-Control $next
    Start-Sleep -Seconds 1
  }
  if (-not $r.Closed) { Shot 'wizard-not-closed' }
  Log ("wizard: " + ($r | ConvertTo-Json -Compress))
  $r
}

# Answers the old-install question and waits for setup to exit. Returns the exit codes it can see.
function Invoke-SetupQuestionOnly([string]$path, [string]$answer) {
  $w = Wait-Until { Get-NsisWindows | Where-Object { -not (Test-Wizard $_) } | Select-Object -First 1 } 60 'old install question'
  if ($null -eq $w) { Shot 'no-question'; return $null }
  $text = Send-MessageBoxAnswer $w $answer
  $null = Wait-Until { (Get-SetupProcesses $path).Count -eq 0 } 60 'setup to exit'
  $text
}

# A message box sits on top of the wizard while both are open, so it comes first.
function Select-NsisWindow($windows) {
  $boxes = @($windows | Where-Object { -not (Test-Wizard $_) })
  $(if ($boxes.Count -gt 0) { $boxes[0] } else { $windows[0] })
}

# Goes through the uninstaller and answers the question whether to delete the user data.
function Invoke-Uninstall([string]$uninstaller, [string]$dataAnswer) {
  $r = [ordered]@{ DataQuestions = @(); Messages = @() }
  if (-not (Test-Path $uninstaller)) { Log "no uninstaller at $uninstaller, nothing to uninstall"; return $r }
  Log "uninstalling with $uninstaller, user data question: $dataAnswer"
  Open-AsUser $uninstaller
  $deadline = (Get-Date).AddMinutes(5)
  $seen = $false
  $last = ''
  while ((Get-Date) -lt $deadline) {
    $windows = @(Get-NsisWindows)
    if ($windows.Count -eq 0) { if ($seen) { break }; Start-Sleep -Milliseconds 500; continue }
    $seen = $true
    $w = Select-NsisWindow $windows
    if (-not (Test-Wizard $w)) {
      if ((Get-Texts $w) -like '*delete your ARCitISA settings*') { $r.DataQuestions += (Send-MessageBoxAnswer $w $dataAnswer) }
      else { $r.Messages += (Send-MessageBoxAnswer $w 'OK') }
      Start-Sleep -Seconds 1; continue
    }
    $texts = Get-Texts $w
    if ($texts -ne $last) { Shot 'uninstall-page'; $last = $texts }
    $next = Find-Id $w '1'
    if ($next -and $next.Current.IsEnabled) { Invoke-Control $next }
    Start-Sleep -Seconds 1
  }
  Start-Sleep -Seconds 3
  Log ("uninstall: " + ($r | ConvertTo-Json -Compress))
  $r
}

function Get-AppWindows {
  $pids = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like '*\ARCitISA\arcitisa.exe' } | ForEach-Object { [int]$_.ProcessId })
  if ($pids.Count -eq 0) { return @() }
  @(Get-WindowAumids $pids)
}
function Stop-NewApp {
  Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like '*\ARCitISA\arcitisa.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
}
function Test-AppStarted([string]$how) {
  $w = Wait-Until { Get-AppWindows | Where-Object { $_.Title -like 'ARCitISA*' } } 60 "app window after $how"
  Shot "app-$how"
  Check "app starts ($how)" ($null -ne $w) ($w | ConvertTo-Json -Compress)
}

# Pins the Start menu shortcut with a taskbar layout policy, then counts taskbar buttons.
function Get-TaskbarButtons {
  $tray = Get-TopWindows | Where-Object { $_.Current.ClassName -eq 'Shell_TrayWnd' } | Select-Object -First 1
  if ($null -eq $tray) { return @() }
  @(Get-Descendants $tray | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and $_.Current.Name -like '*ARCitISA*' } | ForEach-Object { $_.Current.Name })
}

# ---------- Environment ----------
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$uac = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue
Note ("driver elevated: $elevated, EnableLUA: $($uac.EnableLUA), ConsentPromptBehaviorAdmin: $($uac.ConsentPromptBehaviorAdmin), user: $($identity.Name)")
$null = Wait-Until { Get-TopWindows | Where-Object { $_.Current.ClassName -eq 'Shell_TrayWnd' } } 120 'taskbar'
Start-Sleep -Seconds 5
$script:openViaExplorer = $elevated -and $uac.EnableLUA -eq 1
if ($script:openViaExplorer) {
  # A UAC prompt is on the secure desktop, where UI Automation cannot click. Elevate without asking.
  Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' ConsentPromptBehaviorAdmin 0
  Note 'set ConsentPromptBehaviorAdmin to 0 so that elevation needs no click'
} else {
  Note 'UAC is off or this script is not elevated: setup runs with the rights of this script, an all-users install needs no second, elevated setup instance'
}

$oldRc2 = Join-Path $kit 'old-3.4.0-rc.2\ARCitISA-3.4.0-rc.2.Setup.exe'
$old340 = Get-ChildItem (Join-Path $kit 'old-3.4.0') -Filter '*Setup.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
$perUserUninstaller = "$env:LOCALAPPDATA\Programs\ARCitISA\Uninstall ARCitISA.exe"

try {
  switch ($Test) {
    'A' {
      Get-State 'fresh' | Out-Null
      Install-Squirrel $oldRc2 'swate' 'swate'
      $s1 = Get-State 'after 3.4.0-rc.2 install and first run'
      Check 'old install present' ((Test-AppFolder "$env:LOCALAPPDATA\swate") -and (Test-Entry $s1 'swate'))
      Note ("AppData after old install: " + ($s1.AppData -join '; '))
      Start-Setup $rc1
      $wz = Invoke-Wizard $rc1 @{ Question = 'Yes'; Mode = 'user'; Desktop = $false; Run = $true }
      $s2 = Get-State 'after rc.1 setup'
      Check 'question shown once' ($wz.Questions -eq 1) "questions: $($wz.Questions)"
      Check 'no error message' ($wz.Errors.Count -eq 0) ($wz.Errors -join ' / ')
      Check 'old entry gone from Settings > Apps' (-not (Test-Entry $s2 'swate'))
      Check 'no app-* folder in %LocalAppData%\swate' (-not (Test-AppFolder "$env:LOCALAPPDATA\swate")) ($s2.SquirrelSwate -join ',')
      Check 'old Start menu shortcut gone' (@($s2.Links | Where-Object { $_.Target -like '*\swate\*' }).Count -eq 0) (($s2.Links | ForEach-Object { $_.Path + ' -> ' + $_.Target }) -join '; ')
      Check '%AppData%\Swate kept' (Test-Path "$env:APPDATA\Swate")
      Note ("AppData after new setup: " + ($s2.AppData -join '; '))
      $order = (@($wz.Pages | Where-Object { $_ -in 'mode', 'folder', 'shortcuts' }) -join ',')
      Check 'pages in order' ($order -eq 'mode,folder,shortcuts') ($wz.Pages -join ',')
      Check 'desktop checkbox unticked by default' ($wz.Desktop -eq 'Off') "$($wz.Desktop)"
      Check 'new entry in Settings > Apps' (Test-Entry $s2 $guid)
      Check 'no desktop shortcut' (-not (Test-Link $s2 "*\Desktop\ARCitISA.lnk"))
      Check 'Start menu shortcut with app id' (@($s2.Links | Where-Object { $_.Path -like '*Start Menu\Programs\ARCitISA.lnk' -and $_.Aumid -eq $appId }).Count -eq 1)
      Test-AppStarted 'finish page'
      Stop-NewApp
      Note ("AppData at the end: " + ((Get-State 'end').AppData -join '; '))
    }
    'B' {
      Install-Squirrel $oldRc2 'swate' 'swate'
      Start-Setup $rc1
      $text = Invoke-SetupQuestionOnly $rc1 'No'
      $s = Get-State 'after No'
      Check 'question shown' ($null -ne $text) "$text"
      Check 'setup quit' ((Get-SetupProcesses $rc1).Count -eq 0)
      Check 'nothing installed' (-not $s.PerUserApp -and -not (Test-Entry $s $guid))
      Check 'old install untouched' ((Test-AppFolder "$env:LOCALAPPDATA\swate") -and (Test-Entry $s 'swate'))
    }
    'C' {
      Install-Squirrel $oldRc2 'swate' 'swate' $true
      Check 'old app running' ($null -ne (Get-Process swate -ErrorAction SilentlyContinue))
      Start-Setup $rc1
      $wz = Invoke-Wizard $rc1 @{ Question = 'Yes'; Mode = 'user'; Desktop = $false; Run = $false }
      $s1 = Get-State 'after first attempt with the old app running'
      Note ("messages: " + ($wz.Errors -join ' / '))
      Note ("old app still running: " + [bool](Get-Process swate -ErrorAction SilentlyContinue))
      # Squirrel stops the running app. Setup either installs right away or stops with a message,
      # and then the second attempt must install.
      $s2 = $s1
      if ($s1.PerUserApp) {
        Note 'first attempt installed'
        Check 'no error message' ($wz.Errors.Count -eq 0) ($wz.Errors -join ' / ')
      } else {
        Check 'first attempt stopped with a message' ($wz.Errors.Count -ge 1) ($wz.Errors -join ' / ')
        Close-App 'swate'
        Start-Setup $rc1
        $wz2 = Invoke-Wizard $rc1 @{ Question = 'Yes'; Mode = 'user'; Desktop = $false; Run = $false }
        $s2 = Get-State 'after second attempt'
        Check 'second attempt without an error' ($wz2.Errors.Count -eq 0) ("errors: " + ($wz2.Errors -join ' / ') + ", questions: $($wz2.Questions)")
      }
      Check 'new version installed' ($s2.PerUserApp -and (Test-Entry $s2 $guid))
      Check 'old install gone' (-not (Test-Entry $s2 'swate') -and -not (Test-AppFolder "$env:LOCALAPPDATA\swate"))
    }
    'D' {
      Start-Setup $rc1
      $wz = Invoke-Wizard $rc1 @{ Question = 'Yes'; Mode = 'user'; Desktop = $true; Run = $false }
      $s1 = Get-State 'after install with desktop shortcut'
      Check 'no question without an old install' ($wz.Questions -eq 0)
      $link = @($s1.Links | Where-Object { $_.Path -like '*\Desktop\ARCitISA.lnk' })
      Check 'desktop shortcut created' ($link.Count -eq 1) ($link | ConvertTo-Json -Compress)
      Check 'desktop shortcut has the app id' ($link.Count -eq 1 -and $link[0].Aumid -eq $appId)
      Start-Process explorer.exe -ArgumentList "`"$($link[0].Path)`""
      Test-AppStarted 'desktop shortcut'
      Stop-NewApp
      $u = Invoke-Uninstall $perUserUninstaller 'No'
      $s2 = Get-State 'after uninstall'
      Check 'user data question shown once' ($u.DataQuestions.Count -eq 1) ($u.DataQuestions -join ' / ')
      Check 'desktop shortcut removed' (-not (Test-Link $s2 '*\Desktop\ARCitISA.lnk'))
      Check 'Start menu shortcut removed' (-not (Test-Link $s2 '*Start Menu\Programs\ARCitISA.lnk'))
      Check 'program folder removed' (-not (Test-Path "$env:LOCALAPPDATA\Programs\ARCitISA"))
      Check 'entry removed' (-not (Test-Entry $s2 $guid))
      Check 'user data kept' (Test-Path "$env:APPDATA\Swate") ($s2.AppData -join '; ')
    }
    'E' {
      Start-Setup $rc1
      $null = Invoke-Wizard $rc1 @{ Question = 'Yes'; Mode = 'user'; Desktop = $true; Run = $false }
      $s1 = Get-State 'after rc.1 with desktop shortcut'
      Check 'rc.1 desktop shortcut' (Test-Link $s1 '*\Desktop\ARCitISA.lnk')
      $userData = "$env:APPDATA\ARCitISA", "$env:APPDATA\Swate"
      foreach ($d in $userData) { New-Item -ItemType Directory -Force $d | Out-Null; Set-Content "$d\marker.txt" 'user data' }
      Start-Setup $rc2
      $wz = Invoke-Wizard $rc2 @{ Question = 'Yes'; Mode = 'user'; Desktop = $false; Run = $false }
      $s2 = Get-State 'after rc.2 without ticking the box'
      Check 'rc.2 installed' (@($s2.Entries | Where-Object { $_.Key -eq $guid -and $_.Version -eq '4.0.0-rc.2' }).Count -eq 1) (($s2.Entries | ConvertTo-Json -Compress))
      Check 'no message during the update' ($wz.Errors.Count -eq 0 -and $wz.Questions -eq 0) ($wz.Errors -join ' / ')
      Check 'update keeps the user data' (@($userData | Where-Object { Test-Path "$_\marker.txt" }).Count -eq 2)
      Check 'desktop shortcut kept' (Test-Link $s2 '*\Desktop\ARCitISA.lnk')
      Note "rc.2 checkbox initially $($wz.Desktop)"
      $link = @($s2.Links | Where-Object { $_.Path -like '*\Desktop\ARCitISA.lnk' })
      if ($link.Count -eq 1) {
        Start-Process explorer.exe -ArgumentList "`"$($link[0].Path)`""
        Test-AppStarted 'kept desktop shortcut'
        Stop-NewApp
      }
      $u = Invoke-Uninstall $perUserUninstaller 'Yes'
      $s3 = Get-State 'after uninstall'
      Check 'desktop shortcut removed by uninstall' (-not (Test-Link $s3 '*\Desktop\ARCitISA.lnk'))
      Check 'user data question shown once' ($u.DataQuestions.Count -eq 1) ($u.DataQuestions -join ' / ')
      Check 'Yes deletes the user data' (@($userData | Where-Object { Test-Path $_ }).Count -eq 0) ($s3.AppData -join '; ')
    }
    'F' {
      Start-Setup $rc1
      $null = Invoke-Wizard $rc1 @{ Question = 'Yes'; Mode = 'user'; Desktop = $false; Run = $false }
      $s1 = Get-State 'installed'
      $lnk = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\ARCitISA.lnk"
      # Pin the Start menu shortcut with the taskbar layout policy and restart Explorer.
      $layout = Join-Path $env:TEMP 'taskbar-layout.xml'
      @"
<?xml version="1.0" encoding="utf-8"?>
<LayoutModificationTemplate xmlns="http://schemas.microsoft.com/Start/2014/LayoutModification" xmlns:defaultlayout="http://schemas.microsoft.com/Start/2014/FullDefaultLayout" xmlns:start="http://schemas.microsoft.com/Start/2014/StartLayout" xmlns:taskbar="http://schemas.microsoft.com/Start/2014/TaskbarLayout" Version="1">
  <CustomTaskbarLayoutCollection PinListPlacement="Replace">
    <defaultlayout:TaskbarLayout>
      <taskbar:TaskbarPinList>
        <taskbar:DesktopApp DesktopApplicationLinkPath="$lnk" />
      </taskbar:TaskbarPinList>
    </defaultlayout:TaskbarLayout>
  </CustomTaskbarLayoutCollection>
</LayoutModificationTemplate>
"@ | Set-Content -Encoding UTF8 $layout
      New-Item -Force 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer' | Out-Null
      Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer' StartLayoutFile $layout
      Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer' LockedStartLayout 1 -Type DWord
      Stop-Process -Name explorer -Force
      $null = Wait-Until { Get-TopWindows | Where-Object { $_.Current.ClassName -eq 'Shell_TrayWnd' } } 60 'taskbar after restart'
      Start-Sleep -Seconds 10
      $pinned = Get-TaskbarButtons
      Shot 'taskbar-pinned'
      Note ("taskbar buttons before start: " + ($pinned -join ' | '))
      Check 'pinned icon shown' ($pinned.Count -ge 1) ($pinned -join ' | ')

      # 1. Start from the Start menu shortcut, the way the Start menu and the finish page do.
      Start-Process explorer.exe -ArgumentList "`"$lnk`""
      Test-AppStarted 'Start menu shortcut'
      Start-Sleep -Seconds 3
      $b1 = Get-TaskbarButtons
      Shot 'taskbar-start-menu'
      Check 'one taskbar button when started from the shortcut' ($b1.Count -eq 1) ($b1 -join ' | ')
      Stop-NewApp
      Start-Sleep -Seconds 3

      # 2. Start from the pinned taskbar icon.
      $tray = Get-TopWindows | Where-Object { $_.Current.ClassName -eq 'Shell_TrayWnd' } | Select-Object -First 1
      $pin = Get-Descendants $tray | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and $_.Current.Name -like '*ARCitISA*' } | Select-Object -First 1
      # The Invoke pattern of the taskbar button did not start the app in the first run, a click does.
      if ($pin) { Invoke-Click $pin } else { Note 'pinned icon not found' }
      Test-AppStarted 'pinned icon'
      Start-Sleep -Seconds 3
      $b2 = Get-TaskbarButtons
      Shot 'taskbar-pinned-start'
      Check 'one taskbar button when started from the pinned icon' ($b2.Count -eq 1) ($b2 -join ' | ')
      Stop-NewApp
      Start-Sleep -Seconds 3

      # 3. Start the exe directly, as a file association or Explorer would.
      Start-Process explorer.exe -ArgumentList "`"$env:LOCALAPPDATA\Programs\ARCitISA\ARCitISA.exe`""
      Test-AppStarted 'exe'
      Start-Sleep -Seconds 3
      $b3 = Get-TaskbarButtons
      Shot 'taskbar-exe'
      Note ("taskbar buttons when the exe is started directly: " + ($b3 -join ' | '))
      Stop-NewApp
    }
    'G' {
      Install-Squirrel $oldRc2 'swate' 'swate'
      Start-Setup $rc1
      $wz = Invoke-Wizard $rc1 @{ Question = 'Yes'; Mode = 'all'; Desktop = $true; Run = $false }
      $s1 = Get-State 'after all-users install'
      Check 'question shown once' ($wz.Questions -eq 1) "questions: $($wz.Questions)"
      Check 'no error message' ($wz.Errors.Count -eq 0) ($wz.Errors -join ' / ')
      Check 'old install gone' (-not (Test-Entry $s1 'swate') -and -not (Test-AppFolder "$env:LOCALAPPDATA\swate"))
      Check 'installed in Program Files' $s1.AllUsersApp "folder page: $($wz.Folder)"
      Check 'entry in HKLM' (@($s1.Entries | Where-Object { $_.Hive -eq 'HKLM' -and $_.Key -eq $guid }).Count -eq 1)
      Check 'shortcut on the public desktop' (Test-Link $s1 "$env:PUBLIC\Desktop\ARCitISA.lnk")
      Check 'Start menu shortcut for all users' (Test-Link $s1 "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\ARCitISA.lnk")
      $u = Invoke-Uninstall "$env:ProgramFiles\ARCitISA\Uninstall ARCitISA.exe" 'No'
      $s2 = Get-State 'after uninstall'
      Check 'user data question shown once' ($u.DataQuestions.Count -eq 1) ($u.DataQuestions -join ' / ')
      Check 'public desktop shortcut removed' (-not (Test-Link $s2 "$env:PUBLIC\Desktop\ARCitISA.lnk"))
      Check 'program removed' (-not (Test-Path "$env:ProgramFiles\ARCitISA\ARCitISA.exe") -and -not (Test-Entry $s2 $guid))
      Check 'user data kept' (Test-Path "$env:APPDATA\Swate")
    }
    'H' {
      if (-not $old340) { throw 'old-3.4.0 setup missing in the kit' }
      Install-Squirrel $oldRc2 'swate' 'swate'
      Install-Squirrel $old340 'arcitisa' 'arcitisa'
      $s1 = Get-State 'both old versions installed'
      Check 'two old entries' ((Test-Entry $s1 'swate') -and (Test-Entry $s1 'arcitisa'))
      Start-Setup $rc1
      $wz = Invoke-Wizard $rc1 @{ Question = 'Yes'; Mode = 'user'; Desktop = $false; Run = $false }
      $s2 = Get-State 'after rc.1 setup'
      Check 'question shown once' ($wz.Questions -eq 1) "questions: $($wz.Questions)"
      Check 'no error message' ($wz.Errors.Count -eq 0) ($wz.Errors -join ' / ')
      Check 'both old entries gone' (-not (Test-Entry $s2 'swate') -and -not (Test-Entry $s2 'arcitisa'))
      Check 'no app-* folders left' (-not (Test-AppFolder "$env:LOCALAPPDATA\swate") -and -not (Test-AppFolder "$env:LOCALAPPDATA\arcitisa"))
      Check 'new version installed' ($s2.PerUserApp -and (Test-Entry $s2 $guid))
    }
  }
} catch {
  Log "ERROR $($_.Exception.Message) at $($_.InvocationInfo.PositionMessage)"
  $script:result.Error = $_.Exception.Message
  Shot 'error'
} finally {
  $script:result | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $out 'result.json')
  Log 'done'
}
$failed = [bool]$script:result.Error -or @($script:result.Checks | Where-Object { -not $_.Pass }).Count -gt 0
exit $(if ($failed) { 1 } else { 0 })
