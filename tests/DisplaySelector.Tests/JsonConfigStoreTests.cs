using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class JsonConfigStoreTests
{
    [Fact]
    public void Roundtrips_config()
    {
        using var tmp = new TempDir();
        var store = new JsonConfigStore(tmp.File("config.json"), new NullLog());

        store.Save(new AppConfig { AutoStart = true, DebugLogging = true, DefaultProfileId = "abc" });
        var cfg = store.Load();

        Assert.True(cfg.AutoStart);
        Assert.True(cfg.DebugLogging);
        Assert.Equal("abc", cfg.DefaultProfileId);
    }

    [Fact]
    public void Returns_defaults_when_missing()
    {
        using var tmp = new TempDir();
        var store = new JsonConfigStore(tmp.File("config.json"), new NullLog());

        var cfg = store.Load();

        Assert.False(cfg.AutoStart);
        Assert.False(cfg.DebugLogging);
        Assert.Null(cfg.DefaultProfileId);
    }
}
