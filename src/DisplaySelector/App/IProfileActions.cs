using DisplaySelector.Core.Profiles;

namespace DisplaySelector.App;

/// <summary>
/// The profile commands the Profile Manager window drives. Implemented by the controller as thin
/// pass-throughs to the same operations the tray menu uses, so both entry points behave identically.
/// </summary>
internal interface IProfileActions
{
    IReadOnlyList<Profile> Profiles { get; }

    /// <summary>The profile matching the live hardware (as check-marked in the tray menu), or null.</summary>
    string? ActiveProfileId { get; }

    void Activate(string id);

    void Rename(string id);

    void SetHotkey(string id);

    void SetAudio(string id);

    void CreateLauncher(string id);

    /// <summary>A desktop shortcut that only switches to the profile (created straight away, nothing launched).</summary>
    void CreateProfileShortcut(string id);

    void Move(string id, int delta);

    void Delete(string id);

    void SaveCurrent();

    void SaveCurrentAudio();

    /// <summary>Pick the executable first, then the profile it should switch to.</summary>
    void CreateLauncherAndAssign();

    /// <summary>Opens Windows Settings ▸ Display.</summary>
    void OpenDisplaySettings();

    /// <summary>Opens (or brings forward) Profile Diagnostics for the profile.</summary>
    void ShowDiagnostics(string id);

    /// <summary>The window's selection changed (an open Profile Diagnostics follows it).</summary>
    void SelectionChanged(string? id);
}
