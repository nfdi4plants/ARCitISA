# Reads the AppUserModelID (PKEY_AppUserModel_ID) of a window or of a shortcut file. Read-only.
if (-not ('AumidReader' -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class AumidReader {
  [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IPropertyStore {
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
    [PreserveSig] int Commit();
  }

  [StructLayout(LayoutKind.Sequential, Pack = 4)]
  struct PROPERTYKEY { public Guid fmtid; public uint pid; }

  [StructLayout(LayoutKind.Sequential)]
  struct PROPVARIANT { public ushort vt; public ushort r1; public ushort r2; public ushort r3; public IntPtr p; public IntPtr p2; }

  [DllImport("shell32.dll")]
  static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

  [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
  static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindCtx, int flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

  [DllImport("ole32.dll")]
  static extern int PropVariantClear(ref PROPVARIANT value);

  static readonly Guid StoreIid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

  static string Read(IPropertyStore store) {
    var key = new PROPERTYKEY { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };
    PROPVARIANT value;
    if (store.GetValue(ref key, out value) != 0) return "(GetValue failed)";
    try {
      if (value.vt == 0) return "(not set)";
      if (value.vt == 31) return Marshal.PtrToStringUni(value.p);
      return "(vt " + value.vt + ")";
    } finally { PropVariantClear(ref value); }
  }

  public static string ForWindow(IntPtr hwnd) {
    var iid = StoreIid; IPropertyStore store;
    int hr = SHGetPropertyStoreForWindow(hwnd, ref iid, out store);
    return hr != 0 ? "(error 0x" + hr.ToString("X8") + ")" : Read(store);
  }

  public static string ForFile(string path) {
    var iid = StoreIid; IPropertyStore store;
    int hr = SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out store);
    return hr != 0 ? "(error 0x" + hr.ToString("X8") + ")" : Read(store);
  }
}
"@
}

# All visible top-level windows of the given process ids, with their AUMIDs.
function Get-WindowAumids([int[]]$ProcessIds) {
  if (-not ('WindowLister' -as [type])) {
    Add-Type -TypeDefinition @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class WindowLister {
  delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
  public static List<object[]> Visible() {
    var result = new List<object[]>();
    EnumWindows((h, l) => {
      if (IsWindowVisible(h)) { uint pid; GetWindowThreadProcessId(h, out pid); var sb = new StringBuilder(256); GetWindowText(h, sb, 256); result.Add(new object[] { h, (int)pid, sb.ToString() }); }
      return true; }, IntPtr.Zero);
    return result;
  }
}
"@
  }
  foreach ($w in [WindowLister]::Visible()) {
    if ($ProcessIds -contains $w[1] -and $w[2]) {
      [pscustomobject]@{ Pid = $w[1]; Title = $w[2]; Aumid = [AumidReader]::ForWindow([IntPtr]$w[0]) }
    }
  }
}
