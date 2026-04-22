namespace Loupedeck.VoxRingPlugin.Helpers;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
internal static class ClipboardHelper
{
    public static bool SetText(string text)
    {
        bool success = false;
        var thread = new Thread(() => success = SetClipboardText(text));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(3000);
        return success;
    }

    private static bool SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var hGlobal = GlobalAlloc(0x0002, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero) return false;
            var locked = GlobalLock(hGlobal);
            if (locked == IntPtr.Zero) { GlobalFree(hGlobal); return false; }
            Marshal.Copy(text.ToCharArray(), 0, locked, text.Length);
            Marshal.WriteInt16(locked, text.Length * 2, 0);
            GlobalUnlock(hGlobal);
            if (SetClipboardData(13, hGlobal) == IntPtr.Zero) { GlobalFree(hGlobal); return false; }
            return true;
        }
        finally { CloseClipboard(); }
    }

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint f, IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint f, UIntPtr n);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr h);
}
