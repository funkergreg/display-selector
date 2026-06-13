using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class JsonProfileStoreTests
{
    [Fact]
    public void Save_then_Load_roundtrips_full_profile_shape()
    {
        using var tmp = new TempDir();
        var store = new JsonProfileStore(tmp.File("profiles.json"), new NullLog());

        var doc = new ProfilesDocument();
        doc.Profiles.Add(new Profile
        {
            Name = "Couch Gaming",
            Hotkey = new HotkeyBinding { Modifiers = { "Control", "Alt" }, Key = "F10" },
            Audio = new AudioConfig { EndpointId = "{0.0.0.00000000}.{abc}", FriendlyName = "Soundbar" },
            Display = new DisplayConfig
            {
                PathInfo = "AAAA",
                ModeInfo = "BBBB",
                Targets = { new DisplayTarget { StableId = "hdmi:0", Edid = "LGE:5B09", Friendly = "LG TV", Primary = true } },
            },
            CreatedUtc = new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero),
        });

        store.Save(doc);
        var loaded = store.Load();

        Assert.Equal(1, loaded.SchemaVersion);
        var p = Assert.Single(loaded.Profiles);
        Assert.Equal("Couch Gaming", p.Name);
        Assert.Equal("F10", p.Hotkey!.Key);
        Assert.Equal(new[] { "Control", "Alt" }, p.Hotkey.Modifiers);
        Assert.Equal("Soundbar", p.Audio!.FriendlyName);
        Assert.Equal("AAAA", p.Display!.PathInfo);
        var target = Assert.Single(p.Display.Targets);
        Assert.Equal("hdmi:0", target.StableId);
        Assert.True(target.Primary);
    }

    [Fact]
    public void Load_missing_file_returns_empty_document()
    {
        using var tmp = new TempDir();
        var store = new JsonProfileStore(tmp.File("profiles.json"), new NullLog());

        var doc = store.Load();

        Assert.Empty(doc.Profiles);
        Assert.Equal(1, doc.SchemaVersion);
    }

    [Fact]
    public void Second_save_creates_backup()
    {
        using var tmp = new TempDir();
        var path = tmp.File("profiles.json");
        var store = new JsonProfileStore(path, new NullLog());

        store.Save(new ProfilesDocument());
        store.Save(new ProfilesDocument());

        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Load_recovers_from_backup_when_main_is_corrupt()
    {
        using var tmp = new TempDir();
        var path = tmp.File("profiles.json");
        var store = new JsonProfileStore(path, new NullLog());

        var good = new ProfilesDocument();
        good.Profiles.Add(new Profile { Name = "Work" });
        store.Save(good); // writes main
        store.Save(good); // promotes prior main to .bak (also "Work")

        File.WriteAllText(path, "{ not valid json ]"); // corrupt the live file

        var loaded = store.Load();

        var p = Assert.Single(loaded.Profiles);
        Assert.Equal("Work", p.Name);
    }
}
