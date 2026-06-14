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
            if (!TryResolveModifier(modifier, out _, out var flag))
            {
                return false;
            }

            modifiers |= flag;
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

        var parts = binding.Modifiers
            .Select(m => TryResolveModifier(m, out var display, out _) ? display : m)
            .ToList();
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

    // Single source of truth for modifier aliases: maps a stored modifier string to its canonical
    // display name and Win32 flag. Used by both TryParse (flag) and Format (display).
    private static bool TryResolveModifier(string modifier, out string display, out uint flag)
    {
        switch (modifier.Trim().ToLowerInvariant())
        {
            case "control":
            case "ctrl":
                (display, flag) = ("Ctrl", NativeMethods.MOD_CONTROL);
                return true;
            case "alt":
                (display, flag) = ("Alt", NativeMethods.MOD_ALT);
                return true;
            case "shift":
                (display, flag) = ("Shift", NativeMethods.MOD_SHIFT);
                return true;
            case "win":
            case "windows":
                (display, flag) = ("Win", NativeMethods.MOD_WIN);
                return true;
            default:
                (display, flag) = (modifier, 0);
                return false;
        }
    }
}
