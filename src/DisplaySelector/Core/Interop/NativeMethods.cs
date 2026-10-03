using System.Runtime.InteropServices;

namespace DisplaySelector.Core.Interop;

/// <summary>
/// P/Invoke surface. Convention for this repo: source-generated <see cref="LibraryImportAttribute"/>,
/// UTF-16 (<c>*W</c>) entry points, one small file per subsystem. This file holds the window-messaging
/// calls used for single-instance signaling and command forwarding.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>Broadcast target for <see cref="PostMessageW"/> — delivered to top-level windows only.</summary>
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    /// <summary>WS_EX_TOOLWINDOW — keeps the hidden listener window out of Alt-Tab.</summary>
    public const int WS_EX_TOOLWINDOW = 0x0080;

    // Global hotkey messaging.
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Command forwarding to the running instance (game shortcuts): WM_COPYDATA to the listener window.
    public const int WM_COPYDATA = 0x004A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public nuint dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll")]
    public static partial IntPtr SendMessageTimeoutW(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    // Working-set trimming (see MemoryTuning). GetCurrentProcess returns a pseudo-handle;
    // passing (-1, -1) as the min/max asks Windows to trim the working set to the minimum.
    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWorkingSetSize(IntPtr process, nint minimumWorkingSetSize, nint maximumWorkingSetSize);
}
