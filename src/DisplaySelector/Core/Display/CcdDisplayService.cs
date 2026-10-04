using DisplaySelector.Core.Display.Interop;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Display;

/// <summary>
/// CCD implementation of <see cref="IDisplayService"/>. Captures the raw active path/mode arrays
/// (which already encode topology, primary, resolution and orientation) plus a decoded, stable
/// per-target identity used to remap onto live hardware on apply.
/// </summary>
public sealed class CcdDisplayService : IDisplayService
{
    private const uint ApplyFlags =
        CcdNative.SDC_APPLY
        | CcdNative.SDC_USE_SUPPLIED_DISPLAY_CONFIG
        | CcdNative.SDC_SAVE_TO_DATABASE
        | CcdNative.SDC_ALLOW_CHANGES;

    private readonly ILog _log;

    public CcdDisplayService(ILog log) => _log = log;

    public DisplayConfig Capture()
    {
        if (!QueryActive(out var paths, out var modes))
        {
            throw new InvalidOperationException("QueryDisplayConfig failed; cannot capture display configuration.");
        }

        var config = new DisplayConfig
        {
            PathInfo = CcdBlob.Encode(paths),
            ModeInfo = CcdBlob.Encode(modes),
            Targets = DecodeTargets(paths, modes),
        };

        _log.Info($"Captured display config: {config.Targets.Count} target(s), {paths.Length} path(s), {modes.Length} mode(s).");
        foreach (var t in config.Targets)
        {
            _log.Info($"  display '{t.Friendly}' port={t.StableId} res={t.Resolution} rot={t.Orientation} primary={t.Primary}");
        }

        return config;
    }

    public IReadOnlyList<DisplayTarget> GetCurrentDisplays()
    {
        return QueryActive(out var paths, out var modes)
            ? DecodeTargets(paths, modes)
            : Array.Empty<DisplayTarget>();
    }

    public DisplayApplyResult Apply(DisplayConfig config)
    {
        if (string.IsNullOrEmpty(config.PathInfo) || string.IsNullOrEmpty(config.ModeInfo))
        {
            return DisplayApplyResult.Fail("Profile has no captured display configuration.");
        }

        if (!TryDecode(config, out var paths, out var modes))
        {
            return DisplayApplyResult.Fail("Stored display configuration is corrupt.");
        }

        var unavailable = FindUnavailableTargets(config.Targets);
        if (unavailable.Count > 0)
        {
            _log.Info($"Apply: {unavailable.Count} saved target(s) not currently present: {string.Join(", ", unavailable.Select(t => $"{t.Friendly} ({t.StableId})"))}");
        }

        // First attempt: apply the supplied config directly (works in-session and often via the CCD database).
        var hr = CcdNative.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlags);
        if (hr == 0)
        {
            _log.Info("Applied saved display configuration (direct).");
            return DisplayApplyResult.Ok(unavailable);
        }

        _log.Info($"Direct apply failed ({hr:X8}); retrying after adapter-LUID remap.");

        // Cross-session: the saved adapter LUIDs are stale. Rewrite them to the current adapter(s).
        if (TryRemapToCurrentAdapters(paths, modes))
        {
            hr = CcdNative.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlags);
            if (hr == 0)
            {
                _log.Info("Applied saved display configuration (after LUID remap).");
                return DisplayApplyResult.Ok(unavailable);
            }
        }

        _log.Error($"Apply failed: {hr} (0x{hr:X8}).");
        return DisplayApplyResult.Fail($"SetDisplayConfig returned {hr}.", unavailable);
    }

    public bool MatchesCurrent(DisplayConfig config)
    {
        if (!TryDecode(config, out var saved, out var savedModes) ||
            !QueryActive(out var live, out var liveModes) ||
            live.Length != saved.Length)
        {
            return false;
        }

        // After a reboot the saved adapter LUIDs are stale: compare against the current adapter instead.
        // When that isn't possible (multi-GPU) this reports "different", and the profile is simply applied.
        if (saved.Any(s => FindLive(live, s) < 0) && !TryRemapToCurrentAdapters(saved, savedModes, live))
        {
            return false;
        }

        foreach (var s in saved)
        {
            var index = FindLive(live, s);
            if (index < 0)
            {
                return false;
            }

            var l = live[index];
            if (s.targetInfo.rotation != l.targetInfo.rotation ||
                s.targetInfo.scaling != l.targetInfo.scaling ||
                !SameRate(s.targetInfo.refreshRate, l.targetInfo.refreshRate) ||
                SourceModeOf(s, savedModes) is not { } a ||
                SourceModeOf(l, liveModes) is not { } b ||
                a.width != b.width || a.height != b.height ||
                a.position.x != b.position.x || a.position.y != b.position.y)
            {
                return false;
            }
        }

        // Duplicate vs extend: the same targets must share a source in both layouts.
        return SourceGroups(saved).SetEquals(SourceGroups(live));
    }

    // Decodes the saved blobs; false (logged) when they're missing or corrupt.
    private bool TryDecode(DisplayConfig config, out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes)
    {
        paths = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
        modes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();
        if (string.IsNullOrEmpty(config.PathInfo) || string.IsNullOrEmpty(config.ModeInfo))
        {
            return false;
        }

        try
        {
            paths = CcdBlob.Decode<DISPLAYCONFIG_PATH_INFO>(config.PathInfo);
            modes = CcdBlob.Decode<DISPLAYCONFIG_MODE_INFO>(config.ModeInfo);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to decode stored display blobs.", ex);
            return false;
        }
    }

    private static int FindLive(DISPLAYCONFIG_PATH_INFO[] live, DISPLAYCONFIG_PATH_INFO saved) =>
        Array.FindIndex(live, l =>
            l.targetInfo.id == saved.targetInfo.id && l.targetInfo.adapterId == saved.targetInfo.adapterId);

    private static bool SameRate(DISPLAYCONFIG_RATIONAL x, DISPLAYCONFIG_RATIONAL y) =>
        (ulong)x.Numerator * y.Denominator == (ulong)y.Numerator * x.Denominator;

    private static DISPLAYCONFIG_SOURCE_MODE? SourceModeOf(DISPLAYCONFIG_PATH_INFO path, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        var idx = path.sourceInfo.modeInfoIdx;
        return idx < modes.Length && modes[idx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE.Source
            ? modes[idx].modeInfo.sourceMode
            : null;
    }

    // One entry per source: the sorted target ids it drives (several for a duplicated display).
    private static HashSet<string> SourceGroups(DISPLAYCONFIG_PATH_INFO[] paths) =>
        paths
            .GroupBy(p => (p.sourceInfo.adapterId, p.sourceInfo.id))
            .Select(g => string.Join(",", g.Select(p => p.targetInfo.id).OrderBy(id => id)))
            .ToHashSet();

    private bool QueryActive(out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes) =>
        QueryPaths(CcdNative.QDC_ONLY_ACTIVE_PATHS, out paths, out modes);

    private bool QueryPaths(uint flags, out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes)
    {
        paths = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
        modes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();

        var hr = CcdNative.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
        if (hr != 0)
        {
            _log.Error($"GetDisplayConfigBufferSizes failed: {hr} (0x{hr:X8}).");
            return false;
        }

        var p = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var m = new DISPLAYCONFIG_MODE_INFO[modeCount];
        hr = CcdNative.QueryDisplayConfig(flags, ref pathCount, p, ref modeCount, m, IntPtr.Zero);
        if (hr != 0)
        {
            _log.Error($"QueryDisplayConfig failed: {hr} (0x{hr:X8}).");
            return false;
        }

        // The API may return fewer elements than the buffer sizes.
        Array.Resize(ref p, (int)pathCount);
        Array.Resize(ref m, (int)modeCount);
        paths = p;
        modes = m;
        return true;
    }

    private List<DisplayTarget> DecodeTargets(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        var targets = new List<DisplayTarget>();
        foreach (var path in paths)
        {
            var name = GetTargetName(path.targetInfo.adapterId, path.targetInfo.id);
            var (resolution, isPrimary) = ReadSourceMode(path, modes);

            targets.Add(new DisplayTarget
            {
                StableId = PortKey(name.outputTechnology, name.connectorInstance),
                Edid = EdidKey(name),
                Friendly = FriendlyName(name),
                Primary = isPrimary,
                Resolution = resolution,
                Orientation = path.targetInfo.rotation.ToString(),
            });
        }

        return targets;
    }

    private static string FriendlyName(DISPLAYCONFIG_TARGET_DEVICE_NAME name) =>
        string.IsNullOrWhiteSpace(name.monitorFriendlyDeviceName)
            ? name.outputTechnology.ToString()
            : name.monitorFriendlyDeviceName;

    private DISPLAYCONFIG_TARGET_DEVICE_NAME GetTargetName(LUID adapterId, uint id)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GetTargetName,
                size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapterId,
                id = id,
            },
        };

        var hr = CcdNative.DisplayConfigGetDeviceInfo(ref request);
        if (hr != 0)
        {
            _log.Debug($"DisplayConfigGetDeviceInfo(GetTargetName) failed for id={id}: {hr} (0x{hr:X8}).");
        }

        return request;
    }

    private static (string? Resolution, bool IsPrimary) ReadSourceMode(
        DISPLAYCONFIG_PATH_INFO path,
        DISPLAYCONFIG_MODE_INFO[] modes)
    {
        return SourceModeOf(path, modes) is { } source
            ? ($"{source.width}x{source.height}", source.position is { x: 0, y: 0 })
            : (null, false);
    }

    // A saved target is "unavailable" only when it isn't CONNECTED — not merely inactive. Checking
    // active paths here (as this once did) flagged every display the profile was about to turn on
    // (e.g. the TV when switching from the desk), so "Displays not available" fired on every switch.
    private List<DisplayTarget> FindUnavailableTargets(IReadOnlyList<DisplayTarget> savedTargets)
    {
        if (savedTargets.Count == 0)
        {
            return new List<DisplayTarget>();
        }

        var connected = GetConnectedDisplays().Select(d => d.StableId).ToHashSet();
        if (connected.Count == 0)
        {
            return new List<DisplayTarget>(); // query failed — don't guess (matches the previous failure behaviour)
        }

        return savedTargets.Where(t => !connected.Contains(t.StableId)).ToList();
    }

    /// <summary>
    /// Every display Windows reports as connected (active or not). <c>QDC_ALL_PATHS</c> lists every
    /// source×target combination, so targets are de-duplicated before the per-target name lookup;
    /// <c>targetAvailable</c> is false for a target with nothing attached — including a TV that dropped
    /// HDMI hot-plug-detect when powered off, which is exactly the case worth reporting. Empty if the
    /// query fails. (No active-paths fallback: that would bring back the false warning above.)
    /// </summary>
    public IReadOnlyList<DisplayTarget> GetConnectedDisplays()
    {
        var displays = new List<DisplayTarget>();
        if (!QueryPaths(CcdNative.QDC_ALL_PATHS, out var paths, out _))
        {
            return displays;
        }

        var seen = new HashSet<(LUID Adapter, uint Id)>();
        var ports = new HashSet<string>();
        foreach (var path in paths)
        {
            var target = path.targetInfo;
            if (target.targetAvailable == 0 || !seen.Add((target.adapterId, target.id)))
            {
                continue;
            }

            var name = GetTargetName(target.adapterId, target.id);
            var key = PortKey(name.outputTechnology, name.connectorInstance);
            if (ports.Add(key))
            {
                displays.Add(new DisplayTarget
                {
                    StableId = key,
                    Edid = EdidKey(name),
                    Friendly = FriendlyName(name),
                });
            }
        }

        _log.Debug($"Connected display ports: {string.Join(", ", displays.Select(d => d.StableId))}");
        return displays;
    }

    // current: the live active paths, when the caller already queried them.
    private bool TryRemapToCurrentAdapters(
        DISPLAYCONFIG_PATH_INFO[] paths,
        DISPLAYCONFIG_MODE_INFO[] modes,
        DISPLAYCONFIG_PATH_INFO[]? current = null)
    {
        if (current is null && !QueryActive(out current, out _))
        {
            return false;
        }

        var luids = current.Select(p => p.targetInfo.adapterId).Distinct().ToList();

        // M2 supports the single-GPU fast path: replace every stale LUID with the one current adapter.
        // Multi-GPU per-target remap is handled in M3 (activation), where it can be tested across reboots.
        if (luids.Count != 1)
        {
            _log.Info($"LUID remap skipped: {luids.Count} adapters present (single-GPU fast path only in M2).");
            return false;
        }

        var adapter = current[0].targetInfo.adapterId;
        for (var i = 0; i < paths.Length; i++)
        {
            paths[i].sourceInfo.adapterId = adapter;
            paths[i].targetInfo.adapterId = adapter;
        }

        for (var i = 0; i < modes.Length; i++)
        {
            modes[i].adapterId = adapter;
        }

        return true;
    }

    private static string PortKey(DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY tech, uint connectorInstance)
        => $"{tech}:{connectorInstance}";

    private static string? EdidKey(DISPLAYCONFIG_TARGET_DEVICE_NAME name)
    {
        if (!string.IsNullOrWhiteSpace(name.monitorDevicePath))
        {
            return name.monitorDevicePath;
        }

        return name.edidManufactureId == 0 && name.edidProductCodeId == 0
            ? null
            : $"{name.edidManufactureId:X4}-{name.edidProductCodeId:X4}";
    }
}
