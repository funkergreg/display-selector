using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Hotkeys;

/// <summary>
/// Registers system-global hotkeys (RegisterHotKey) and raises <see cref="HotkeyPressed"/> with the
/// caller's id when one fires. Registration FAILS (returns false) if another app owns the combo —
/// it cannot be overridden (see CLAUDE.md / DESIGN hotkey notes).
/// </summary>
public interface IHotkeyService
{
    event Action<int> HotkeyPressed;

    bool TryRegister(int id, HotkeyBinding binding);

    void Unregister(int id);

    void UnregisterAll();
}
