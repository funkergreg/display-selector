using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.UI;

/// <summary>
/// The small icon beside each Profile: a monitor for a full Profile, a speaker for an audio-only one.
/// The shape carries the meaning (Section 508: never color alone); the color reinforces it. Both colors
/// clear 4.5:1 contrast on the light menu/list background and stay apart for every kind of color
/// blindness (blue vs dark vermillion). Drawn from the Windows 11 icon font, so it's crisp at any DPI.
/// </summary>
internal static class ProfileGlyphs
{
    private const string Monitor = "\uE7F4"; // TVMonitor
    private const string Speaker = "\uE767"; // Volume

    // Brushes, not colors: list rows repaint often, so don't allocate one per paint.
    private static readonly Brush FullBrush = new SolidBrush(Color.FromArgb(0x00, 0x72, 0xB2));      // blue, 5.2:1 on white
    private static readonly Brush AudioOnlyBrush = new SolidBrush(Color.FromArgb(0xC0, 0x50, 0x00)); // dark vermillion, 4.8:1

    // Segoe Fluent Icons ships with Windows 11; Segoe MDL2 Assets (same code points) is the fallback.
    private static readonly FontFamily? IconFamily = FindFamily("Segoe Fluent Icons", "Segoe MDL2 Assets");

    private static readonly Dictionary<(bool AudioOnly, int Px), Image> Cache = new();
    private static readonly Dictionary<int, Font> Fonts = new(); // per pixel size; list rows repaint often
    private static readonly StringFormat Centered = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    /// <summary>The glyph as an image, for menus (cached per size). Null if no icon font is installed.</summary>
    public static Image? For(Profile profile, int px)
    {
        if (IconFamily is null || px <= 0)
        {
            return null;
        }

        var key = (profile.IsAudioOnly, px);
        if (!Cache.TryGetValue(key, out var image))
        {
            var bitmap = new Bitmap(px, px);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                Render(graphics, profile, new Rectangle(0, 0, px, px), BrushOf(profile));
            }
            Cache[key] = image = bitmap;
        }

        return image;
    }

    /// <summary>Row height for an owner-drawn Profile list (owner-drawn rows don't scale themselves).</summary>
    public static int RowHeight(ListBox list) => list.Font.Height + list.LogicalToDeviceUnits(8);

    /// <summary>
    /// Draws one Profile row of an owner-drawn list, starting <paramref name="left"/> device px in: the
    /// glyph, then the label (the Profile Manager and the pickers, so they always match). On a selected row
    /// both take the highlight text color, so the glyph stays visible; the shape still tells them apart.
    /// </summary>
    public static void DrawRow(Control list, DrawItemEventArgs e, Profile profile, string label, int left)
    {
        var selected = (e.State & DrawItemState.Selected) != 0;
        var bounds = e.Bounds;
        var glyph = Math.Min(list.LogicalToDeviceUnits(16), bounds.Height);
        Render(
            e.Graphics,
            profile,
            new Rectangle(bounds.X + left, bounds.Y + (bounds.Height - glyph) / 2, glyph, glyph),
            selected ? SystemBrushes.HighlightText : BrushOf(profile));

        DrawLabel(e, label, left + glyph + list.LogicalToDeviceUnits(6), selected ? SystemColors.HighlightText : SystemColors.WindowText);
    }

    /// <summary>A row's label, <paramref name="left"/> device px in (also the lists' "no Profiles" row).</summary>
    public static void DrawLabel(DrawItemEventArgs e, string label, int left, Color color)
    {
        var bounds = e.Bounds;
        TextRenderer.DrawText(
            e.Graphics,
            label,
            e.Font,
            new Rectangle(bounds.X + left, bounds.Y, bounds.Width - left, bounds.Height),
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static Brush BrushOf(Profile profile) => profile.IsAudioOnly ? AudioOnlyBrush : FullBrush;

    private static void Render(Graphics graphics, Profile profile, Rectangle cell, Brush brush)
    {
        if (IconFamily is null)
        {
            return;
        }

        var px = Math.Min(cell.Width, cell.Height);
        if (!Fonts.TryGetValue(px, out var font))
        {
            Fonts[px] = font = new Font(IconFamily, px * 0.8f, GraphicsUnit.Pixel);
        }

        var oldHint = graphics.TextRenderingHint;
        var oldSmoothing = graphics.SmoothingMode;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawString(profile.IsAudioOnly ? Speaker : Monitor, font, brush, cell, Centered);
        graphics.TextRenderingHint = oldHint;
        graphics.SmoothingMode = oldSmoothing;
    }

    private static FontFamily? FindFamily(params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                return new FontFamily(name);
            }
            catch (ArgumentException)
            {
                // Not installed; try the next.
            }
        }

        return null;
    }
}
