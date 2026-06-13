using DisplaySelector.Core.Hotkeys;
using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class HotkeyCodecTests
{
    [Fact]
    public void Parses_modifiers_and_function_key()
    {
        var binding = new HotkeyBinding { Modifiers = { "Control", "Alt" }, Key = "F10" };

        Assert.True(HotkeyCodec.TryParse(binding, out var mods, out var vk));
        Assert.Equal(0x0002u | 0x0001u, mods); // MOD_CONTROL | MOD_ALT
        Assert.Equal(0x79u, vk); // VK_F10
    }

    [Fact]
    public void Fails_on_empty_key()
    {
        var binding = new HotkeyBinding { Modifiers = { "Control" }, Key = "" };

        Assert.False(HotkeyCodec.TryParse(binding, out _, out _));
    }

    [Fact]
    public void Fails_on_unknown_modifier()
    {
        var binding = new HotkeyBinding { Modifiers = { "Hyper" }, Key = "F9" };

        Assert.False(HotkeyCodec.TryParse(binding, out _, out _));
    }

    [Fact]
    public void Formats_human_readable()
    {
        var binding = new HotkeyBinding { Modifiers = { "ctrl", "alt" }, Key = "F10" };

        Assert.Equal("Ctrl+Alt+F10", HotkeyCodec.Format(binding));
    }

    [Theory]
    [InlineData("A", false, true)]   // bare letter -> risky
    [InlineData("F10", false, false)] // bare function key -> fine (our defaults)
    [InlineData("A", true, false)]   // letter WITH a modifier -> fine
    public void Risky_detection(string key, bool withModifier, bool expectedRisky)
    {
        var binding = new HotkeyBinding { Key = key };
        if (withModifier)
        {
            binding.Modifiers.Add("Control");
        }

        Assert.Equal(expectedRisky, HotkeyCodec.IsRisky(binding));
    }
}
