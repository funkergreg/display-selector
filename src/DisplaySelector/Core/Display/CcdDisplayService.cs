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

    public bool ValidateCurrent()
    {
        if (!QueryActive(out var paths, out var modes))
        {
            return false;
        }

        var hr = CcdNative.SetDisplayConfig(
            (uint)paths.Length,
            paths,
            (uint)modes.Length,
            modes,
            CcdNative.SDC_VALIDATE | CcdNative.SDC_USE_SUPPLIED_DISPLAY_CONFIG);

        if (hr != 0)
        {
            _log.Error($"SetDisplayConfig(VALIDATE) failed: {hr} (0x{hr:X8}).");
        }

        return hr == 0;
    }

    public DisplayApplyResult ReapplyCurrent()
    {
        if (!QueryActive(out var paths, out var modes))
        {
            return DisplayApplyResult.Fail("QueryDisplayConfig failed.");
        }

        var hr = CcdNative.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlags);
        if (hr == 0)
        {
            _log.Info("Re-applied current display configuration (unstick).");
            return DisplayApplyResult.Ok();
        }

        _log.Error($"ReapplyCurrent failed: {hr} (0x{hr:X8}).");
        return DisplayApplyResult.Fail($"SetDisplayConfig returned {hr}.");
    }

    public DisplayApplyResult Apply(DisplayConfig config)
    {
        if (string.IsNullOrEmpty(config.PathInfo) || string.IsNullOrEmpty(config.ModeInfo))
        {
            return DisplayApplyResult.Fail("Profile has no captured display configuration.");
        }

        DISPLAYCONFIG_PATH_INFO[] paths;
        DISPLAYCONFIG_MODE_INFO[] modes;
        try
        {
            paths = CcdBlob.Decode<DISPLAYCONFIG_PATH_INFO>(config.PathInfo);
            modes = CcdBlob.Decode<DISPLAYCONFIG_MODE_INFO>(config.ModeInfo);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to decode stored display blobs.", ex);
            return DisplayApplyResult.Fail("Stored display configuration is corrupt.");
        }

        var unavailable = FindUnavailableTargets(config.Targets);
        if (unavailable.Count > 0)
        {
            _log.Info($"Apply: {unavailable.Count} saved target(s) not currently present: {string.Join(", ", unavailable)}");
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

    private bool QueryActive(out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes)
    {
        paths = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
        modes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();

        var hr = CcdNative.GetDisplayConfigBufferSizes(CcdNative.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount);
        if (hr != 0)
        {
            _log.Error($"GetDisplayConfigBufferSizes failed: {hr} (0x{hr:X8}).");
            return false;
        }

        var p = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var m = new DISPLAYCONFIG_MODE_INFO[modeCount];
        hr = CcdNative.QueryDisplayConfig(CcdNative.QDC_ONLY_ACTIVE_PATHS, ref pathCount, p, ref modeCount, m, IntPtr.Zero);
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
                Friendly = string.IsNullOrWhiteSpace(name.monitorFriendlyDeviceName)
                    ? name.outputTechnology.ToString()
                    : name.monitorFriendlyDeviceName,
                Primary = isPrimary,
                Resolution = resolution,
                Orientation = path.targetInfo.rotation.ToString(),
            });
        }

        return targets;
    }

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
        var idx = path.sourceInfo.modeInfoIdx;
        if (idx == CcdNative.DISPLAYCONFIG_PATH_MODE_IDX_INVALID || idx >= modes.Length)
        {
            return (null, false);
        }

        var mode = modes[idx];
        if (mode.infoType != DISPLAYCONFIG_MODE_INFO_TYPE.Source)
        {
            return (null, false);
        }

        var source = mode.modeInfo.sourceMode;
        var resolution = $"{source.width}x{source.height}";
        var isPrimary = source.position is { x: 0, y: 0 };
        return (resolution, isPrimary);
    }

    private List<string> FindUnavailableTargets(IReadOnlyList<DisplayTarget> savedTargets)
    {
        if (savedTargets.Count == 0 || !QueryActive(out var paths, out _))
        {
            return new List<string>();
        }

        var present = new HashSet<string>();
        foreach (var path in paths)
        {
            var name = GetTargetName(path.targetInfo.adapterId, path.targetInfo.id);
            present.Add(PortKey(name.outputTechnology, name.connectorInstance));
        }

        return savedTargets
            .Where(t => !present.Contains(t.StableId))
            .Select(t => t.Friendly)
            .ToList();
    }

    private bool TryRemapToCurrentAdapters(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        if (!QueryActive(out var current, out _))
        {
            return false;
        }

        var luids = current
            .Select(p => (p.targetInfo.adapterId.LowPart, p.targetInfo.adapterId.HighPart))
            .Distinct()
            .ToList();

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
