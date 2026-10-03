using DisplaySelector.Core;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class DisplayInventoryTests
{
    private static readonly DisplayTarget Tv = new() { StableId = "Hdmi:0", Friendly = "SAMSUNG", Resolution = "1920x1080", Orientation = "Identity", Primary = true };
    private static readonly DisplayTarget Monitor = new() { StableId = "Dp:1", Friendly = "CB272" };

    [Fact]
    public void Rows_list_active_displays_then_connected_ones_not_in_use_once_each()
    {
        var connectedTv = new DisplayTarget { StableId = "Hdmi:0", Friendly = "SAMSUNG" }; // no layout details

        var rows = DisplayInventory.Rows(new[] { Tv }, new[] { connectedTv, Monitor });

        Assert.Equal(new[] { ("Hdmi:0", DisplayState.Active), ("Dp:1", DisplayState.Connected) }, rows.Select(r => (r.Target.StableId, r.State)));
        Assert.Same(Tv, rows[0].Target); // the active entry keeps resolution/primary
    }

    [Fact]
    public void State_is_active_then_connected_then_not_connected()
    {
        Assert.Equal(DisplayState.Active, DisplayInventory.StateOf("Hdmi:0", new[] { Tv }, Array.Empty<DisplayTarget>()));
        Assert.Equal(DisplayState.Connected, DisplayInventory.StateOf("Dp:1", new[] { Tv }, new[] { Monitor }));
        Assert.Equal(DisplayState.NotConnected, DisplayInventory.StateOf("Dvi:0", new[] { Tv }, new[] { Monitor }));
    }

    [Fact]
    public void Line_matches_the_diagnostics_format_and_skips_missing_details()
    {
        Assert.Equal("SAMSUNG | port=Hdmi:0 | 1920x1080 | Identity | PRIMARY", DisplayInventory.Line(Tv));
        Assert.Equal("CB272 | port=Dp:1", DisplayInventory.Line(Monitor));
    }
}

public class ProfileReportTests
{
    [Fact]
    public void Shows_saved_state_next_to_the_live_state_and_the_desktop_items()
    {
        var profile = new Profile
        {
            Name = "TV",
            Hotkey = new HotkeyBinding { Key = "F10" },
            Display = new DisplayConfig
            {
                Targets =
                {
                    new DisplayTarget { StableId = "Hdmi:0", Friendly = "SAMSUNG", Resolution = "3840x2160", Primary = true },
                    new DisplayTarget { StableId = "Dp:1", Friendly = "CB272" },
                },
            },
            Audio = new AudioConfig { EndpointId = "{tv}", FriendlyName = "LG TV" },
        };

        var report = ProfileReport.Build(
            profile,
            isActive: false,
            activeDisplays: Array.Empty<DisplayTarget>(),
            connectedDisplays: new[] { new DisplayTarget { StableId = "Hdmi:0" } },
            audioDevices: new[] { new AudioEndpoint("{tv}", "LG TV", IsDefault: false) },
            shortcutPaths: new[] { @"C:\Desktop\Game (TV).lnk" });

        Assert.Contains("Hotkey  : F10", report);
        Assert.Contains("Status  : Not active", report);
        Assert.Contains("SAMSUNG | port=Hdmi:0 | 3840x2160 | PRIMARY | Connected, not in use", report);
        Assert.Contains("CB272 | port=Dp:1 | Not connected", report);
        Assert.Contains("LG TV | Available", report);
        Assert.Contains(@"Game (TV) | C:\Desktop\Game (TV).lnk", report);
    }

    [Fact]
    public void Audio_only_profile_with_a_missing_device_and_no_desktop_items()
    {
        var profile = new Profile { Name = "Headset", Audio = new AudioConfig { EndpointId = "{hs}", FriendlyName = "Headset" } };

        var report = ProfileReport.Build(
            profile, isActive: true, Array.Empty<DisplayTarget>(), Array.Empty<DisplayTarget>(),
            Array.Empty<AudioEndpoint>(), Array.Empty<string>());

        Assert.Contains("Status  : Active (check-marked)", report);
        Assert.Contains("Hotkey  : None", report);
        Assert.Contains("Audio only", report);
        Assert.Contains("Headset | Not available (off or unplugged)", report);
        Assert.Contains("(none found)", report);
    }
}

public class DiagnosticsReportTests
{
    [Fact]
    public void Marks_the_audio_device_in_use_and_the_active_displays_up_front()
    {
        var display = new ScriptedDisplayService(new[] { new DisplayTarget { StableId = "Hdmi:0", Friendly = "SAMSUNG", Primary = true } })
        {
            Connects = (1, "Dp:1"),
        };
        var audio = new StubAudioService
        {
            Devices = new[]
            {
                new AudioEndpoint("{spk}", "Speakers", IsDefault: true),
                new AudioEndpoint("{tv}", "LG TV", IsDefault: false),
            },
        };

        var report = DiagnosticsReport.Build(display, audio, "Desk");

        Assert.Contains("Active Profile : Desk", report);
        Assert.Contains("  [x] SAMSUNG | port=Hdmi:0 | PRIMARY", report);
        Assert.Contains("  [ ]  | port=Dp:1", report); // connected, not in use (the fake has no friendly name)
        Assert.Contains("  [x] Speakers | {spk}", report);
        Assert.Contains("  [ ] LG TV | {tv}", report);
    }
}
