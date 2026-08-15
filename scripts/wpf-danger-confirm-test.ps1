# ASCII-only danger-confirm regression v2 (elevated): detects MessageBox via Win32
# EnumWindows + GetWindowText (UIA cannot enumerate it), answers Cancel/No via WM_COMMAND.
# NEVER confirms a destructive action.
$resultFile = Join-Path $PSScriptRoot '..\docs\uia-danger-result.txt'
$log = New-Object System.Collections.Generic.List[string]
$failures = 0

function C([int[]]$codes) { return (-join ($codes | ForEach-Object { [char]$_ })) }

function Record([string]$name, [string]$verdict, [string]$detail = "") {
    if ($verdict -eq "FAIL") { $script:failures += 1 }
    $line = $verdict + "  " + $name
    if ($detail) { $line += " :: " + $detail }
    $script:log.Add($line)
}

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinDlg
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    public static List<IntPtr> FindWindows(uint targetPid) {
        var result = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid && IsWindowVisible(h)) result.Add(h);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static string TitleOf(IntPtr h) {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, 256);
        return sb.ToString();
    }
}
"@

function Find-Dialog([uint32]$targetPid, [IntPtr]$mainHwnd, [int]$timeoutMs) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    do {
        foreach ($h in [WinDlg]::FindWindows($targetPid)) {
            if ($h -eq $mainHwnd) { continue }
            $title = [WinDlg]::TitleOf($h)
            if ($title -match "CAELUS") { return $h }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    return [IntPtr]::Zero
}

$NAV_PREFIX = C @(0x5BFC,0x822A,0xFF1A)
$NAV_SETTINGS = C @(0x8BBE,0x7F6E)
$NAV_ENV = C @(0x7CFB,0x7EDF,0x73AF,0x5883)
$BTN_RESTORE = C @(0x6062,0x590D,0x6240,0x6709,0x5DF2,0x8BB0,0x5F55,0x7684,0x7CFB,0x7EDF,0x9879)
$BTN_DEFENDER = (C @(0x6253,0x5F00)) + " Defender " + (C @(0x6392,0x9664,0x8BF4,0x660E))
$DLG_DEFENDER = "Defender " + (C @(0x626B,0x63CF,0x6392,0x9664))
$ROW_SUFFIX = " Defender " + (C @(0x6392,0x9664))
$VBS_TITLE = (C @(0x5173,0x95ED)) + " VBS + " + (C @(0x505C,0x7528)) + " hypervisor" + (C @(0xFF08,0x9700,0x91CD,0x542F,0xFF09))
$BTN_CLOSE = C @(0x5173,0x95ED)
$ID_YES = 6
$ID_NO = 7
$ID_OK = 1
$ID_CANCEL = 2

$log.Add("=== high-risk confirmation regression v2 @ " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " ===")

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    foreach ($proc in @(Get-Process CaelusWpf -ErrorAction SilentlyContinue)) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
    Start-Sleep -Milliseconds 600

    $proc = Start-Process -FilePath (Join-Path $PSScriptRoot '..\wpf\bin\Release\CaelusWpf.exe') -PassThru
    $deadline = [DateTime]::UtcNow.AddMilliseconds(15000)
    do {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    } while ($proc.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    $mainHwnd = $proc.MainWindowHandle
    $appPid = [uint32]$proc.Id
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($mainHwnd)
    [WinDlg]::SetForegroundWindow($mainHwnd) | Out-Null
    Start-Sleep -Milliseconds 1000

    function Find-Element([string]$name) {
        foreach ($el in ($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))) {
            if ($el.Current.Name -eq $name) { return $el }
        }
        return $null
    }

    function Select-Nav([string]$name) {
        $el = Find-Element ($NAV_PREFIX + $name)
        if ($null -eq $el) { return $false }
        $pat = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) {
            $pat.Select(); return $true
        }
        return $false
    }

    function Scroll-AncestorToBottom($el) {
        $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
        $node = $el
        while ($null -ne $node) {
            $sp = $null
            if ($node.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$sp)) {
                try { $sp.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100.0); Start-Sleep -Milliseconds 800; return $true } catch { }
            }
            $node = $walker.GetParent($node)
        }
        return $false
    }

    function Click-Element($el, [switch]$NoScroll) {
        if (-not $NoScroll) { Scroll-AncestorToBottom $el | Out-Null }
        $r = $el.Current.BoundingRectangle
        if ($r.Width -le 1 -or $r.Height -le 1) { return $false }
        $cx = [int]($r.Left + $r.Width / 2); $cy = [int]($r.Top + $r.Height / 2)
        [WinDlg]::SetForegroundWindow($mainHwnd) | Out-Null
        Start-Sleep -Milliseconds 300
        [WinDlg]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 120
        [WinDlg]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [WinDlg]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        return $true
    }

    function Get-ToggleState($el) {
        $pat = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) {
            return $pat.Current.ToggleState
        }
        return $null
    }

    # ---- T1 restore-all: confirm must appear; answer No; nothing executes ----
    $t1 = "T1 restore-all confirm+cancel"
    if (-not (Select-Nav $NAV_SETTINGS)) { Record $t1 "FAIL" "nav settings not found" }
    else {
        Start-Sleep -Milliseconds 600
        $btn = Find-Element $BTN_RESTORE
        if ($null -eq $btn) { Record $t1 "FAIL" "restore button not found" }
        else {
            $clicked = Click-Element $btn
            $dlg = Find-Dialog $appPid $mainHwnd 5000
            if ($dlg -eq [IntPtr]::Zero) { Record $t1 "FAIL" "confirmation dialog did not appear (clicked=$clicked)" }
            else {
                $title = [WinDlg]::TitleOf($dlg)
                [WinDlg]::SendMessage($dlg, 0x0111, [IntPtr]$ID_NO, [IntPtr]::Zero) | Out-Null  # WM_COMMAND IDNO
                Start-Sleep -Milliseconds 800
                $gone = ((Find-Dialog $appPid $mainHwnd 2500) -eq [IntPtr]::Zero)
                if ($gone) { Record $t1 "PASS" "dialog '" + $title + "' appeared and answered No" }
                else { Record $t1 "FAIL" "dialog still present after No" }
            }
        }
    }

    # ---- T2 VBS: only when toggle is Off (turning On triggers confirm); On skips ----
    $t2 = "T2 VBS disable confirm+cancel+rollback"
    if (-not (Select-Nav $NAV_ENV)) { Record $t2 "FAIL" "nav env not found" }
    else {
        Start-Sleep -Milliseconds 600
        $toggle = Find-Element $VBS_TITLE
        if ($null -eq $toggle) { Record $t2 "FAIL" "VBS toggle not found" }
        else {
            $state = Get-ToggleState $toggle
            if ($state -eq [System.Windows.Automation.ToggleState]::Off) {
                Click-Element $toggle -NoScroll | Out-Null
                $dlg = Find-Dialog $appPid $mainHwnd 5000
                if ($dlg -eq [IntPtr]::Zero) { Record $t2 "FAIL" "VBS confirmation dialog did not appear" }
                else {
                    [WinDlg]::SendMessage($dlg, 0x0111, [IntPtr]$ID_CANCEL, [IntPtr]::Zero) | Out-Null
                    Start-Sleep -Milliseconds 900
                    $gone = ((Find-Dialog $appPid $mainHwnd 2500) -eq [IntPtr]::Zero)
                    $after = Get-ToggleState $toggle
                    $rolledBack = ($after -eq [System.Windows.Automation.ToggleState]::Off)
                    if ($gone -and $rolledBack) { Record $t2 "PASS" "confirm shown, cancelled, toggle rolled back" }
                    else { Record $t2 "FAIL" "dialogGone=$gone rolledBack=$rolledBack" }
                }
            } else {
                Record $t2 "SKIP" "VBS already disabled (toggle On); turning Off executes without confirm by design"
            }
        }
    }

    # ---- T3 Defender: open dialog; only test add-direction confirm (cancel) ----
    $t3 = "T3 defender add-exclusion confirm+cancel"
    if (-not (Select-Nav $NAV_SETTINGS)) { Record $t3 "FAIL" "nav settings not found" }
    else {
        Start-Sleep -Milliseconds 600
        $btn = Find-Element $BTN_DEFENDER
        if ($null -eq $btn) { Record $t3 "FAIL" "defender button not found" }
        else {
            Click-Element $btn | Out-Null
            $dlg = [IntPtr]::Zero
            $deadline = [DateTime]::UtcNow.AddMilliseconds(8000)
            do {
                foreach ($h in [WinDlg]::FindWindows($appPid)) {
                    if ([WinDlg]::TitleOf($h) -eq $DLG_DEFENDER) { $dlg = $h; break }
                }
                if ($dlg -ne [IntPtr]::Zero) { break }
                Start-Sleep -Milliseconds 200
            } while ([DateTime]::UtcNow -lt $deadline)

            if ($dlg -eq [IntPtr]::Zero) { Record $t3 "FAIL" "defender dialog did not appear" }
            else {
                Start-Sleep -Milliseconds 1000
                $dlgElement = [System.Windows.Automation.AutomationElement]::FromHandle($dlg)
                $candidate = $null
                foreach ($el in ($dlgElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition))) {
                    if ($el.Current.Name -notlike ("*" + $ROW_SUFFIX)) { continue }
                    $st = Get-ToggleState $el
                    if ($st -eq [System.Windows.Automation.ToggleState]::Off) { $candidate = $el; break }
                }
                if ($null -eq $candidate) { Record $t3 "SKIP" "no non-excluded row available (removal path has no confirm by design)" }
                else {
                    Click-Element $candidate -NoScroll | Out-Null
                    $mb = Find-Dialog $appPid $mainHwnd 5000
                    if ($mb -eq [IntPtr]::Zero) { Record $t3 "FAIL" "defender confirmation dialog did not appear" }
                    else {
                        [WinDlg]::SendMessage($mb, 0x0111, [IntPtr]$ID_CANCEL, [IntPtr]::Zero) | Out-Null
                        Start-Sleep -Milliseconds 900
                        $gone = ((Find-Dialog $appPid $mainHwnd 2500) -eq [IntPtr]::Zero)
                        $after = Get-ToggleState $candidate
                        $unchanged = ($after -eq [System.Windows.Automation.ToggleState]::Off)
                        if ($gone -and $unchanged) { Record $t3 "PASS" "confirm shown, cancelled, toggle unchanged" }
                        else { Record $t3 "FAIL" "dialogGone=$gone unchanged=$unchanged" }
                    }
                }
                # close defender dialog
                [WinDlg]::SendMessage($dlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # WM_CLOSE
                Start-Sleep -Milliseconds 600
            }
        }
    }
}
catch {
    Record "runtime" "FAIL" $_.Exception.ToString()
}
finally {
    try { $proc.CloseMainWindow() | Out-Null } catch { }
    Start-Sleep -Milliseconds 500
    if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { } }
    $log.Add("----")
    $log.Add("TOTAL " + $log.Count + "  FAIL " + $failures)
    [System.IO.File]::WriteAllLines($resultFile, $log, (New-Object System.Text.UTF8Encoding($false)))
}
