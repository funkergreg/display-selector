using DisplaySelector.Core.Display.Interop;
using Xunit;

namespace DisplaySelector.Tests;

/// <summary>
/// The blob (de)serialization is what restores a profile's display state, so it must be byte-exact.
/// </summary>
public class CcdBlobTests
{
    [Fact]
    public void Encode_then_decode_preserves_path_fields()
    {
        var paths = new[]
        {
            new DISPLAYCONFIG_PATH_INFO
            {
                sourceInfo = new DISPLAYCONFIG_PATH_SOURCE_INFO
                {
                    adapterId = new LUID { LowPart = 0x1234, HighPart = 7 },
                    id = 3,
                    modeInfoIdx = 1,
                },
                targetInfo = new DISPLAYCONFIG_PATH_TARGET_INFO
                {
                    id = 42,
                    outputTechnology = DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.Hdmi,
                    rotation = DISPLAYCONFIG_ROTATION.Rotate90,
                },
                flags = 1,
            },
        };

        var decoded = CcdBlob.Decode<DISPLAYCONFIG_PATH_INFO>(CcdBlob.Encode(paths));

        var p = Assert.Single(decoded);
        Assert.Equal(0x1234u, p.sourceInfo.adapterId.LowPart);
        Assert.Equal(7, p.sourceInfo.adapterId.HighPart);
        Assert.Equal(3u, p.sourceInfo.id);
        Assert.Equal(42u, p.targetInfo.id);
        Assert.Equal(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.Hdmi, p.targetInfo.outputTechnology);
        Assert.Equal(DISPLAYCONFIG_ROTATION.Rotate90, p.targetInfo.rotation);
        Assert.Equal(1u, p.flags);
    }

    [Fact]
    public void Encode_then_decode_preserves_source_mode_resolution()
    {
        var modes = new[]
        {
            new DISPLAYCONFIG_MODE_INFO
            {
                infoType = DISPLAYCONFIG_MODE_INFO_TYPE.Source,
                id = 5,
                modeInfo = new DISPLAYCONFIG_MODE_INFO_UNION
                {
                    sourceMode = new DISPLAYCONFIG_SOURCE_MODE
                    {
                        width = 3840,
                        height = 2160,
                        position = new POINTL { x = 0, y = 0 },
                    },
                },
            },
        };

        var decoded = CcdBlob.Decode<DISPLAYCONFIG_MODE_INFO>(CcdBlob.Encode(modes));

        var m = Assert.Single(decoded);
        Assert.Equal(DISPLAYCONFIG_MODE_INFO_TYPE.Source, m.infoType);
        Assert.Equal(3840u, m.modeInfo.sourceMode.width);
        Assert.Equal(2160u, m.modeInfo.sourceMode.height);
    }
}
