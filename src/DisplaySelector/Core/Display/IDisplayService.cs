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

    /// <summary>Validate (without applying) that the current configuration is settable — exercises the apply path safely.</summary>
    bool ValidateCurrent();

    /// <summary>Re-apply the current configuration (the "unstick a frozen Windows display UI" fix).</summary>
    DisplayApplyResult ReapplyCurrent();

    /// <summary>Apply a saved configuration, remapping onto live hardware (port-first / LUID fixup). Best-effort.</summary>
    DisplayApplyResult Apply(DisplayConfig config);
}
