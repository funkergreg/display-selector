using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Display;

/// <summary>What Windows can tell about a display: showing desktop, connected but unused, or not there.</summary>
public enum DisplayState
{
    Active,
    Connected,
    NotConnected,
}

/// <summary>A display and its state, for the Display Tester and the diagnostics reports.</summary>
public sealed record DisplayRow(DisplayTarget Target, DisplayState State);

/// <summary>
/// Shared display wording for the Display Tester, Copy diagnostics and Profile diagnostics, so a display
/// reads the same everywhere. "Connected" is Windows' hot-plug view, not power: Windows can't tell whether
/// a display is switched on, and a TV that's off may drop off the list or stay connected.
/// </summary>
public static class DisplayInventory
{
    /// <summary>Active displays (with their layout details) first, then connected ones not in use.</summary>
    public static IReadOnlyList<DisplayRow> Rows(IReadOnlyList<DisplayTarget> active, IReadOnlyList<DisplayTarget> connected) =>
        active.Select(d => new DisplayRow(d, DisplayState.Active))
            .Concat(connected
                .Where(c => active.All(a => a.StableId != c.StableId))
                .Select(c => new DisplayRow(c, DisplayState.Connected)))
            .ToList();

    public static DisplayState StateOf(string stableId, IReadOnlyList<DisplayTarget> active, IReadOnlyList<DisplayTarget> connected) =>
        active.Any(a => a.StableId == stableId) ? DisplayState.Active
        : connected.Any(c => c.StableId == stableId) ? DisplayState.Connected
        : DisplayState.NotConnected;

    public static string Describe(DisplayState state) => state switch
    {
        DisplayState.Active => "Active",
        DisplayState.Connected => "Connected, not in use",
        _ => "Not connected",
    };

    /// <summary>One display on one line, e.g. <c>SAMSUNG | port=Hdmi:0 | 1920x1080 | Identity | PRIMARY</c>.</summary>
    public static string Line(DisplayTarget target)
    {
        var parts = new List<string> { target.Friendly, $"port={target.StableId}" };
        if (target.Resolution is { } resolution)
        {
            parts.Add(resolution);
        }
        if (target.Orientation is { } orientation)
        {
            parts.Add(orientation);
        }
        if (target.Primary)
        {
            parts.Add("PRIMARY");
        }

        return string.Join(" | ", parts);
    }
}
