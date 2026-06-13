using System.Runtime.InteropServices;
using DisplaySelector.Core.Display.Interop;
using Xunit;

namespace DisplaySelector.Tests;

/// <summary>
/// Guards the hand-written CCD struct layouts. These sizes are fixed by the OS ABI; if a field type
/// or order drifts, marshalling corrupts memory — so assert the exact byte sizes up front.
/// </summary>
public class CcdLayoutTests
{
    [Fact]
    public void Path_info_is_72_bytes() =>
        Assert.Equal(72, Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>());

    [Fact]
    public void Mode_info_is_64_bytes() =>
        Assert.Equal(64, Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO>());

    [Fact]
    public void Mode_union_is_48_bytes() =>
        Assert.Equal(48, Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO_UNION>());

    [Fact]
    public void Video_signal_info_is_48_bytes() =>
        Assert.Equal(48, Marshal.SizeOf<DISPLAYCONFIG_VIDEO_SIGNAL_INFO>());

    [Fact]
    public void Target_device_name_is_420_bytes() =>
        Assert.Equal(420, Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>());
}
