using System.Windows.Forms;
using DisplaySelector.Core.Profiles;
using DisplaySelector.UI;
using Xunit;

namespace DisplaySelector.Tests;

public class ProfileGlyphsTests
{
    [Fact]
    public void Full_and_audio_only_profiles_get_different_cached_glyphs()
    {
        var full = ProfileGlyphs.For(new Profile { Display = new DisplayConfig() }, 16);
        var audioOnly = ProfileGlyphs.For(new Profile(), 16);
        if (full is null)
        {
            return; // no Windows icon font on this machine (headless agent): nothing to draw
        }

        Assert.NotSame(full, audioOnly);
        Assert.Same(full, ProfileGlyphs.For(new Profile { Display = new DisplayConfig() }, 16));
        Assert.Equal(16, full.Width);
    }

    [Fact]
    public void Disposing_a_menu_leaves_the_shared_glyph_usable()
    {
        // The tray menu is rebuilt (and the old one disposed) on every change; the cached glyph lives on.
        var glyph = ProfileGlyphs.For(new Profile(), 20);
        if (glyph is null)
        {
            return;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("TV sound") { Image = glyph });
        menu.Dispose();

        Assert.Equal(20, glyph.Width); // throws if the image was disposed with the menu
    }
}
