namespace DisplaySelector.UI;

/// <summary>Sizes a window to its content, so the user doesn't have to resize or scroll to read it.</summary>
internal static class WindowFit
{
    /// <summary>
    /// Makes the client area <paramref name="wantedClient"/> (capped to the monitor's working area). With
    /// <paramref name="growOnly"/> (live updates) a window never shrinks under the user, only grows to fit
    /// more, and is kept on screen.
    /// </summary>
    public static void Fit(Form form, Size wantedClient, bool growOnly)
    {
        var area = Screen.FromControl(form).WorkingArea;
        var frame = form.Size - form.ClientSize; // borders + title bar
        var wanted = wantedClient + frame;
        var size = new Size(Math.Min(wanted.Width, area.Width), Math.Min(wanted.Height, area.Height));
        if (growOnly)
        {
            size = new Size(Math.Max(size.Width, form.Width), Math.Max(size.Height, form.Height));
        }

        if (size == form.Size)
        {
            return;
        }

        form.Size = size;
        form.Location = new Point(
            Math.Clamp(form.Left, area.Left, Math.Max(area.Left, area.Right - form.Width)),
            Math.Clamp(form.Top, area.Top, Math.Max(area.Top, area.Bottom - form.Height)));
    }
}
