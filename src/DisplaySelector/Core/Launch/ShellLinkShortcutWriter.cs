using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.Core.Launch;

/// <summary>
/// Writes <c>.lnk</c> files via the shell's <c>IShellLinkW</c> + <c>IPersistFile</c> COM objects (built
/// into Windows, no dependency), and reads them back. All shell-link interop lives in this file.
/// </summary>
public sealed partial class ShellLinkShortcutWriter : IShortcutWriter
{
    // INFOTIPSIZE: SetDescription fails beyond this.
    private const int MaxDescription = 1023;

    private readonly ILog _log;

    public ShellLinkShortcutWriter(ILog log)
    {
        _log = log;
    }

    public void Write(ShortcutSpec spec)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            Check(link.SetPath(spec.TargetPath), "SetPath");
            Check(link.SetArguments(spec.Arguments), "SetArguments");
            if (!string.IsNullOrEmpty(spec.WorkingDirectory))
            {
                Check(link.SetWorkingDirectory(spec.WorkingDirectory), "SetWorkingDirectory");
            }
            var description = spec.Description.Length > MaxDescription ? spec.Description[..MaxDescription] : spec.Description;
            Check(link.SetDescription(description), "SetDescription");
            Check(link.SetIconLocation(spec.IconPath, 0), "SetIconLocation");

            ((IPersistFile)link).Save(spec.Path, fRemember: true);
            _log.Info($"Wrote shortcut '{spec.Path}' → '{spec.TargetPath}' {spec.Arguments}");
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public IReadOnlyList<string>? TryReadArguments(string path)
    {
        try
        {
            return SplitArguments(Read(path).Arguments);
        }
        catch (Exception ex)
        {
            _log.Debug($"Couldn't read shortcut '{path}' ({ex.Message}).");
            return null;
        }
    }

    /// <summary>Splits an argument string exactly as Windows hands it to a process (<c>CommandLineToArgvW</c>).</summary>
    internal static string[] SplitArguments(string arguments)
    {
        // A dummy argv[0]: CommandLineToArgvW parses the first token with program-name rules.
        var argv = CommandLineToArgvW("app.exe " + arguments, out var count);
        if (argv == IntPtr.Zero)
        {
            throw new COMException("CommandLineToArgvW failed.", Marshal.GetHRForLastWin32Error());
        }

        try
        {
            var result = new string[Math.Max(0, count - 1)];
            for (var i = 1; i < count; i++)
            {
                result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            }

            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    /// <summary>Reads back a shortcut's target, arguments and description (integration tests / diagnostics).</summary>
    internal static (string Target, string Arguments, string Description) Read(string path)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0 /* STGM_READ */);
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var description = new StringBuilder(MaxDescription + 1);
            Check(link.GetPath(target, target.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */), "GetPath");
            Check(link.GetArguments(arguments, arguments.Capacity), "GetArguments");
            Check(link.GetDescription(description, description.Capacity), "GetDescription");
            return (target.ToString(), arguments.ToString(), description.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static void Check(int hr, string call)
    {
        if (hr < 0)
        {
            throw new COMException($"IShellLinkW.{call} failed (0x{hr:X8}).", hr);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr hMem);

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")] // CLSID_ShellLink
    private class ShellLink
    {
    }

    // Every vtable slot up to SetPath must be declared in order; the slot offset is what matters.
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")] // IID_IShellLinkW
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        [PreserveSig]
        int GetIDList(out IntPtr ppidl);

        [PreserveSig]
        int SetIDList(IntPtr pidl);

        [PreserveSig]
        int GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        [PreserveSig]
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        [PreserveSig]
        int GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        [PreserveSig]
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        [PreserveSig]
        int GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        [PreserveSig]
        int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        [PreserveSig]
        int GetHotkey(out short pwHotkey);

        [PreserveSig]
        int SetHotkey(short wHotkey);

        [PreserveSig]
        int GetShowCmd(out int piShowCmd);

        [PreserveSig]
        int SetShowCmd(int iShowCmd);

        [PreserveSig]
        int GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        [PreserveSig]
        int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        [PreserveSig]
        int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        [PreserveSig]
        int Resolve(IntPtr hwnd, uint fFlags);

        [PreserveSig]
        int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
