using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Display;

/// <summary>
/// Display capture/apply behind an interface so the fragile CCD interop (see <c>Interop/CcdNative.cs</c>)
/// stays mockable and swappable (CLAUDE.md "here be dragons" #1).
/// </summary>
public interface IDisplayService
{
    /// <summary>Capture the current display configuration as a persistable <see cref="DisplayConfig"/>.</summary>
    DisplayConfig Capture();

    /// <summary>Decode the current displays for diagnostics (stable key, EDID, friendly, primary, resolution, orientation).</summary>
    IReadOnlyList<DisplayTarget> GetCurrentDisplays();

    /// <summary>
    /// Every connected display, active or not (port key, EDID key, friendly name; no layout details).
    /// "Connected" is Windows' hot-plug view, not power: a TV that's off may or may not be listed.
    /// Empty if the query fails.
    /// </summary>
    IReadOnlyList<DisplayTarget> GetConnectedDisplays();

    /// <summary>Apply a saved configuration, remapping onto live hardware (port-first / LUID fixup). Best-effort.</summary>
    DisplayApplyResult Apply(DisplayConfig config);

    /// <summary>
    /// True when the live layout is the saved one in every detail the saved configuration holds: the same
    /// displays, duplicate/extend grouping, desktop positions, resolutions, rotation, scaling and refresh
    /// rate. False whenever in doubt (a stale or undecodable capture), so the caller applies instead.
    /// </summary>
    bool MatchesCurrent(DisplayConfig config);
}
