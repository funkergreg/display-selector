using System.Runtime.InteropServices;

namespace DisplaySelector.Core.Audio.Interop;

/// <summary>
/// HERE BE DRAGONS: <c>IPolicyConfig</c> is an UNDOCUMENTED Windows COM interface — the only known way
/// to programmatically set the default audio endpoint. Everything in this file is isolated so it can be
/// swapped if a future Windows build breaks it (CLAUDE.md "here be dragons" #2).
///
/// Only <see cref="IPolicyConfig.SetDefaultEndpoint"/> is used, but every preceding vtable slot MUST be
/// declared (in order) so the slot offset is correct. The unused methods use <see cref="IntPtr"/>
/// placeholders — parameter types are irrelevant for slots we never call; only the method order matters.
/// </summary>
internal static class PolicyConfig
{
    /// <summary>Windows audio roles. Set all three so every app + System Sounds follows the change.</summary>
    public enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    public static readonly ERole[] AllRoles =
    {
        ERole.Console,
        ERole.Multimedia,
        ERole.Communications,
    };

    /// <summary>Creates the policy-config COM object. Caller must <see cref="Marshal.FinalReleaseComObject"/> it.</summary>
    public static IPolicyConfig CreateClient() => (IPolicyConfig)new PolicyConfigClient();
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] // CLSID_CPolicyConfigClient
internal class PolicyConfigClient
{
}

[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")] // IID_IPolicyConfig
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat(IntPtr deviceName, IntPtr format);

    [PreserveSig]
    int GetDeviceFormat(IntPtr deviceName, int bDefault, IntPtr format);

    [PreserveSig]
    int ResetDeviceFormat(IntPtr deviceName);

    [PreserveSig]
    int SetDeviceFormat(IntPtr deviceName, IntPtr endpointFormat, IntPtr mixFormat);

    [PreserveSig]
    int GetProcessingPeriod(IntPtr deviceName, int bDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod(IntPtr deviceName, IntPtr period);

    [PreserveSig]
    int GetShareMode(IntPtr deviceName, IntPtr mode);

    [PreserveSig]
    int SetShareMode(IntPtr deviceName, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue(IntPtr deviceName, IntPtr key, IntPtr value);

    [PreserveSig]
    int SetPropertyValue(IntPtr deviceName, IntPtr key, IntPtr value);

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PolicyConfig.ERole role);

    [PreserveSig]
    int SetEndpointVisibility(IntPtr deviceName, int visible);
}
