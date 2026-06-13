using DisplaySelector.Core.Logging;

namespace DisplaySelector.Core.Profiles;

/// <summary><see cref="IConfigStore"/> over <c>config.json</c>, with the same .bak recovery as the profile store.</summary>
public sealed class JsonConfigStore : IConfigStore
{
    private readonly string _path;
    private readonly ILog _log;

    public JsonConfigStore(string path, ILog log)
    {
        _path = path;
        _log = log;
    }

    public AppConfig Load()
    {
        try
        {
            var cfg = AtomicJsonFile.TryRead<AppConfig>(_path);
            if (cfg is not null)
            {
                _log.Info($"Loaded config: autoStart={cfg.AutoStart}, debugLogging={cfg.DebugLogging}");
                return cfg;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"config.json unreadable ({ex.Message}); trying .bak", ex);
        }

        try
        {
            var bak = AtomicJsonFile.TryRead<AppConfig>(AtomicJsonFile.BackupPath(_path));
            if (bak is not null)
            {
                _log.Info("Recovered config from .bak");
                return bak;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"config.json.bak unreadable ({ex.Message}); using defaults", ex);
        }

        _log.Info("No config found; using defaults");
        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        AtomicJsonFile.Write(_path, config);
        _log.Info($"Saved config: autoStart={config.AutoStart}, debugLogging={config.DebugLogging}");
    }
}
