namespace DisplaySelector.Core.Profiles;

/// <summary>Root document persisted to <c>profiles.json</c>. <see cref="SchemaVersion"/> enables migrations.</summary>
public sealed class ProfilesDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<Profile> Profiles { get; set; } = new();
}

/// <summary>A captured display + audio configuration bound to an optional hotkey.</summary>
public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public HotkeyBinding? Hotkey { get; set; }

    public DisplayConfig? Display { get; set; }

    public AudioConfig? Audio { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}

/// <summary>Modifiers + key, e.g. <c>{ Modifiers: ["Control","Alt"], Key: "F10" }</c>.</summary>
public sealed class HotkeyBinding
{
    public List<string> Modifiers { get; set; } = new();

    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// Captured display state. The raw CCD blobs (<see cref="PathInfo"/>/<see cref="ModeInfo"/>) are
/// populated by the display service in M2; <see cref="Targets"/> carries the stable identity used
/// to remap onto live hardware (port-first, EDID fallback — see CLAUDE.md "here be dragons").
/// </summary>
public sealed class DisplayConfig
{
    public string? PathInfo { get; set; }

    public string? ModeInfo { get; set; }

    public List<DisplayTarget> Targets { get; set; } = new();
}

public sealed class DisplayTarget
{
    /// <summary>Port-first key (outputTechnology + connectorInstance).</summary>
    public string StableId { get; set; } = string.Empty;

    /// <summary>EDID-derived fallback key (monitor device path / manufacturer + product), when available.</summary>
    public string? Edid { get; set; }

    public string Friendly { get; set; } = string.Empty;

    public bool Primary { get; set; }

    /// <summary>Descriptive only (the authoritative state lives in the CCD blob), e.g. "3840x2160".</summary>
    public string? Resolution { get; set; }

    /// <summary>Descriptive only, e.g. "Identity" / "Rotate90".</summary>
    public string? Orientation { get; set; }
}

/// <summary>Captured default-output endpoint. We store only the device, never volume (by design).</summary>
public sealed class AudioConfig
{
    public string EndpointId { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;
}

/// <summary>App-wide settings persisted to <c>config.json</c>.</summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    public bool AutoStart { get; set; }

    public bool DebugLogging { get; set; }

    public string? DefaultProfileId { get; set; }
}
