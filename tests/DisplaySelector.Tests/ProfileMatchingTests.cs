using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class ProfileMatchingTests
{
    private static DisplayTarget Target(string id, bool primary = false, string? resolution = null) =>
        new() { StableId = id, Primary = primary, Resolution = resolution };

    private static DisplayConfig Layout(params DisplayTarget[] targets) => new() { Targets = targets.ToList() };

    [Fact]
    public void Layout_matches_regardless_of_order_and_resolution()
    {
        var saved = Layout(Target("Hdmi:0", primary: true, "3840x2160"), Target("Dp:1"));
        var live = new[] { Target("Dp:1", resolution: "2560x1440"), Target("Hdmi:0", primary: true, "1920x1080") };

        Assert.True(ProfileMatching.DisplayLayoutMatches(saved, live));
    }

    [Fact]
    public void Different_primary_does_not_match()
    {
        var saved = Layout(Target("Hdmi:0", primary: true), Target("Dp:1"));
        var live = new[] { Target("Hdmi:0"), Target("Dp:1", primary: true) };

        Assert.False(ProfileMatching.DisplayLayoutMatches(saved, live));
    }

    [Fact]
    public void Extra_or_missing_display_does_not_match()
    {
        var saved = Layout(Target("Hdmi:0", primary: true));
        var live = new[] { Target("Hdmi:0", primary: true), Target("Dp:1") };

        Assert.False(ProfileMatching.DisplayLayoutMatches(saved, live));
        Assert.False(ProfileMatching.DisplayLayoutMatches(Layout(Target("Hdmi:0", true), Target("Dp:1")), new[] { Target("Hdmi:0", true) }));
    }

    [Fact]
    public void FindActive_requires_both_layout_and_audio()
    {
        var tv = new Profile
        {
            Name = "TV",
            Display = Layout(Target("Hdmi:0", primary: true)),
            Audio = new AudioConfig { EndpointId = "{tv}" },
        };
        var headset = new Profile { Name = "Headset", Audio = new AudioConfig { EndpointId = "{hs}" } };
        var live = new[] { Target("Hdmi:0", primary: true) };

        Assert.Same(tv, ProfileMatching.FindActive(new[] { tv, headset }, live, "{tv}"));
        Assert.Same(headset, ProfileMatching.FindActive(new[] { tv, headset }, live, "{hs}"));
        Assert.Null(ProfileMatching.FindActive(new[] { tv }, live, "{other}"));
    }

    [Fact]
    public void FindActive_prefers_the_live_profile_among_coarse_matches()
    {
        var display = new DisplayConfig { Targets = { new DisplayTarget { StableId = "Hdmi:0", Primary = true } } };
        var tv4K = new Profile { Name = "TV 4K", Display = display };
        var tv1080 = new Profile { Name = "TV 1080", Display = display };
        var live = new[] { new DisplayTarget { StableId = "Hdmi:0", Primary = true } };

        Assert.Same(tv4K, ProfileMatching.FindActive(new[] { tv4K, tv1080 }, live, null));
        Assert.Same(tv1080, ProfileMatching.FindActive(new[] { tv4K, tv1080 }, live, null, p => p == tv1080));
        Assert.Same(tv4K, ProfileMatching.FindActive(new[] { tv4K, tv1080 }, live, null, _ => false)); // none live → first
    }
}
