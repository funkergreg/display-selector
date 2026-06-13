using DisplaySelector.Core.Interop;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Hotkeys;

/// <summary>
/// Translates a stored <see cref="HotkeyBinding"/> to Win32 modifier flags + virtual-key code and back,
/// and flags "risky" bindings (a typing key with no modifier would hijack normal input).
/// Pure and WinForms-light so it is unit-testable.
/// </summary>
public static class HotkeyCodec
{
    public static bool TryParse(HotkeyBinding? binding, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (binding is null || string.IsNullOrWhiteSpace(binding.Key))
        {
            return false;
        }

        foreach (var modifier in binding.Modifiers)
        {
            switch (modifier.Trim().ToLowerInvariant())
            {
                case "control":
                case "ctrl":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        if (!Enum.TryParse<Keys>(binding.Key, ignoreCase: true, out var key))
        {
            return false;
        }

        virtualKey = (uint)(key & Keys.KeyCode);
        return virtualKey != 0;
    }

    public static string Format(HotkeyBinding? binding)
    {
        if (binding is null || string.IsNullOrWhiteSpace(binding.Key))
        {
            return "(none)";
        }

        var parts = binding.Modifiers.Select(Normalize).ToList();
        parts.Add(binding.Key);
        return string.Join("+", parts);
    }

    /// <summary>A binding with no modifier and a non-function key would intercept ordinary typing.</summary>
    public static bool IsRisky(HotkeyBinding? binding)
    {
        if (binding is null || binding.Modifiers.Count > 0)
        {
            return false;
        }

        if (Enum.TryParse<Keys>(binding.Key, ignoreCase: true, out var key))
        {
            var code = key & Keys.KeyCode;
            var isFunctionKey = code is >= Keys.F1 and <= Keys.F24;
            return !isFunctionKey;
        }

        return false;
    }

    private static string Normalize(string modifier) => modifier.Trim().ToLowerInvariant() switch
    {
        "control" or "ctrl" => "Ctrl",
        "alt" => "Alt",
        "shift" => "Shift",
        "win" or "windows" => "Win",
        _ => modifier,
    };
}
