using System.Runtime.InteropServices;

namespace DisplaySelector.Core.Interop;

/// <summary>
/// P/Invoke surface. Convention for this repo: source-generated <see cref="LibraryImportAttribute"/>,
/// UTF-16 (<c>*W</c>) entry points, one small file per subsystem. This file holds the window-messaging
/// calls used for single-instance signaling.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>Broadcast target for <see cref="PostMessageW"/> — delivered to top-level windows only.</summary>
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    /// <summary>WS_EX_TOOLWINDOW — keeps the hidden listener window out of Alt-Tab.</summary>
    public const int WS_EX_TOOLWINDOW = 0x0080;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
