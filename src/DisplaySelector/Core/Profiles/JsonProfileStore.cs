using System.Text.Json;
using DisplaySelector.Core.Logging;

namespace DisplaySelector.Core.Profiles;

/// <summary>
/// <see cref="IProfileStore"/> over <c>profiles.json</c>. Logs the resolved data on load/save
/// (full JSON at Debug level) so the data shape is inspectable during integration/active tests.
/// </summary>
public sealed class JsonProfileStore : IProfileStore
{
    private readonly string _path;
    private readonly ILog _log;

    public JsonProfileStore(string path, ILog log)
    {
        _path = path;
        _log = log;
    }

    public ProfilesDocument Load()
    {
        var (doc, source) = LoadWithRecovery();

        _log.Info($"Loaded profiles from {source}: schemaVersion={doc.SchemaVersion}, count={doc.Profiles.Count}");
        foreach (var p in doc.Profiles)
        {
            _log.Info(
                $"  profile '{p.Name}' id={p.Id} hotkey={DescribeHotkey(p.Hotkey)} " +
                $"audio={p.Audio?.FriendlyName ?? "-"} displays={p.Display?.Targets.Count ?? 0}");
        }

        if (_log.Level == LogLevel.Debug)
        {
            _log.Debug("profiles.json => " + JsonSerializer.Serialize(doc, AtomicJsonFile.Options));
        }

        return doc;
    }

    public void Save(ProfilesDocument document)
    {
        AtomicJsonFile.Write(_path, document);
        _log.Info($"Saved profiles.json: schemaVersion={document.SchemaVersion}, count={document.Profiles.Count}");

        if (_log.Level == LogLevel.Debug)
        {
            _log.Debug("profiles.json <= " + JsonSerializer.Serialize(document, AtomicJsonFile.Options));
        }
    }

    private (ProfilesDocument Document, string Source) LoadWithRecovery()
    {
        try
        {
            var doc = AtomicJsonFile.TryRead<ProfilesDocument>(_path);
            if (doc is not null)
            {
                return (doc, "profiles.json");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"profiles.json unreadable ({ex.Message}); trying .bak", ex);
        }

        try
        {
            var bak = AtomicJsonFile.TryRead<ProfilesDocument>(AtomicJsonFile.BackupPath(_path));
            if (bak is not null)
            {
                _log.Info("Recovered profiles from .bak");
                return (bak, "profiles.json.bak");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"profiles.json.bak unreadable ({ex.Message}); starting empty", ex);
        }

        return (new ProfilesDocument(), "new (empty)");
    }

    private static string DescribeHotkey(HotkeyBinding? h)
    {
        if (h is null)
        {
            return "-";
        }

        return h.Modifiers.Count == 0 ? h.Key : string.Join("+", h.Modifiers) + "+" + h.Key;
    }
}
