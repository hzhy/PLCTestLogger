# 对运行中的 PlcLogger 截图：主界面、新增设备对话框、查询窗口。
# 用法: powershell -File tools/screenshot.ps1 -OutDir docs/manual/images
param([string]$OutDir = "docs/manual/images")

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Native {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder sb, int max);
  public static IntPtr FindByTitle(string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      var sb = new System.Text.StringBuilder(256); GetWindowTextW(h, sb, 256);
      if (sb.ToString() == title) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@
[void][Native]::SetProcessDPIAware()
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Capture([IntPtr]$h, [string]$name) {
  [Native]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 400
  $r = New-Object Native+RECT
  if ([Native]::DwmGetWindowAttribute($h, 9, [ref]$r, 16) -ne 0) { [Native]::GetWindowRect($h, [ref]$r) | Out-Null }
  $w = $r.R - $r.L; $hh = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $hh))
  $g.Dispose()
  $path = Join-Path $OutDir "$name.png"
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  "saved $path ($w x $hh)"
}

function ClickButton([IntPtr]$h, [string]$name) {
  [Native]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 300
  $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
  $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $name
  $btn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
  if ($null -eq $btn) { throw "button '$name' not found" }
  $pt = $btn.GetClickablePoint()
  [Native]::SetCursorPos([int]$pt.X, [int]$pt.Y) | Out-Null; Start-Sleep -Milliseconds 200
  [Native]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [Native]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}

function WaitWindow([string]$title, [int]$timeoutMs = 5000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  do { $h = [Native]::FindByTitle($title); if ($h -ne [IntPtr]::Zero) { return $h }; Start-Sleep -Milliseconds 200 } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
  throw "window '$title' not found"
}

$main = WaitWindow "实验数据追溯系统" 15000
[Native]::ShowWindow($main, 9) | Out-Null
Capture $main "main"

ClickButton $main "新增设备"
$dlg = WaitWindow "新增设备" 8000
Start-Sleep -Milliseconds 300
Capture $dlg "add-device"
[Native]::PostMessage($dlg, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Milliseconds 500

ClickButton $main "查询记录"
$q = WaitWindow "查询保存记录"
Start-Sleep -Milliseconds 500
ClickButton $q "查询"
Start-Sleep -Milliseconds 1500
Capture $q "search"
[Native]::PostMessage($q, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
"done"
