using System.Text;
using DisplaySelector.Core.Launch;
using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class ProfileResolverTests
{
    private static readonly Profile Tv = new() { Id = "aaaa", Name = "Living Room TV" };
    private static readonly Profile Desk = new() { Id = "bbbb", Name = "Desk" };

    [Fact]
    public void Resolves_by_id()
    {
        Assert.Same(Desk, ProfileResolver.Resolve(new[] { Tv, Desk }, "bbbb"));
    }

    [Fact]
    public void Resolves_by_name_case_insensitively()
    {
        Assert.Same(Tv, ProfileResolver.Resolve(new[] { Tv, Desk }, "living room tv"));
    }

    [Fact]
    public void Id_wins_over_a_profile_whose_name_equals_that_id()
    {
        var confusing = new Profile { Id = "cccc", Name = "aaaa" };

        Assert.Same(Tv, ProfileResolver.Resolve(new[] { confusing, Tv }, "aaaa"));
    }

    [Fact]
    public void Unknown_reference_is_null()
    {
        Assert.Null(ProfileResolver.Resolve(new[] { Tv, Desk }, "deleted-profile"));
    }
}

public class ShortcutNamingTests
{
    [Fact]
    public void Default_name_combines_target_and_profile()
    {
        Assert.Equal("witcher3 (TV)", ShortcutNaming.DefaultName("TV", @"C:\Games\witcher3.exe"));
    }

    [Fact]
    public void Default_name_for_switch_only_is_the_profile_name()
    {
        Assert.Equal("TV", ShortcutNaming.DefaultName("TV", null));
    }

    [Fact]
    public void Default_name_for_a_uri_is_generic()
    {
        Assert.Equal("Game (TV)", ShortcutNaming.DefaultName("TV", "steam://rungameid/570"));
    }

    [Fact]
    public void Sanitize_strips_invalid_characters_and_trailing_dots()
    {
        Assert.Equal("Game TV", ShortcutNaming.Sanitize("Game: TV?*. "));
    }
}

public class ShortcutRegistryTests
{
    [Fact]
    public void Records_paths_deduplicated_and_prunes_missing_shortcuts()
    {
        using var dir = new TempDir();
        var list = dir.File("shortcuts.txt");
        var a = dir.File("a.lnk");
        var b = dir.File("b.lnk");
        File.WriteAllText(a, "x");
        File.WriteAllText(b, "x");
        var registry = new ShortcutRegistry(list, new NullLog());

        registry.Record(a);
        registry.Record(b);
        registry.Record(a.ToUpperInvariant()); // same file, different case → no duplicate
        File.Delete(b);                         // user deleted it → dropped on the next write
        registry.Record(a);

        Assert.Equal(new[] { a }, File.ReadAllLines(list), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Writes_utf8_with_bom_for_the_inno_uninstaller()
    {
        using var dir = new TempDir();
        var list = dir.File("shortcuts.txt");
        var shortcut = dir.File("Igra Ćevapi (TV).lnk");
        File.WriteAllText(shortcut, "x");

        new ShortcutRegistry(list, new NullLog()).Record(shortcut);

        var bytes = File.ReadAllBytes(list);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Contains(shortcut, File.ReadAllText(list));
    }

    [Fact]
    public void Never_throws_when_the_list_cannot_be_written()
    {
        using var dir = new TempDir();
        var list = dir.File("shortcuts.txt");
        Directory.CreateDirectory(list); // a directory where the file should be → write fails

        new ShortcutRegistry(list, new NullLog()).Record(dir.File("a.lnk"));
    }

    [Fact]
    public void Tracked_paths_are_the_live_ones_and_forget_removes_entries()
    {
        using var dir = new TempDir();
        var list = dir.File("shortcuts.txt");
        var a = dir.File("a.lnk");
        var b = dir.File("b.lnk");
        File.WriteAllText(a, "x");
        File.WriteAllText(b, "x");
        var registry = new ShortcutRegistry(list, new NullLog());
        registry.Record(a);
        registry.Record(b);

        File.Delete(b);
        Assert.Equal(new[] { a }, registry.TrackedPaths());

        registry.Forget(new[] { a.ToUpperInvariant() });
        Assert.Empty(registry.TrackedPaths());
    }
}

public class ProfileShortcutCleanupTests
{
    private static readonly Profile Tv = new() { Id = "tv-id", Name = "TV" };
    private static readonly Profile Desk = new() { Id = "desk-id", Name = "Desk" };

    [Fact]
    public void Finds_the_tracked_shortcuts_that_switch_to_the_profile_and_deletes_them()
    {
        using var dir = new TempDir();
        var registry = new ShortcutRegistry(dir.File("shortcuts.txt"), new NullLog());
        var writer = new FakeShortcutReader();
        var tvShortcut = Tracked(dir, registry, writer, "TV.lnk", new LaunchCommand("tv-id"));
        var tvGame = Tracked(dir, registry, writer, "Game (TV).lnk", new LaunchCommand("TV", @"C:\g.exe")); // by name
        Tracked(dir, registry, writer, "Desk.lnk", new LaunchCommand("desk-id"));
        Tracked(dir, registry, writer, "Unreadable.lnk", null);
        var cleanup = new ProfileShortcutCleanup(registry, writer, new NullLog());

        var found = cleanup.Find(Tv, new[] { Tv, Desk });

        Assert.Equal(new[] { tvShortcut, tvGame }, found);
        Assert.Equal(2, cleanup.Delete(found));
        Assert.False(File.Exists(tvShortcut));
        Assert.DoesNotContain(tvShortcut, registry.TrackedPaths());
        Assert.Contains(dir.File("Desk.lnk"), registry.TrackedPaths());
    }

    private static string Tracked(TempDir dir, ShortcutRegistry registry, FakeShortcutReader writer, string name, LaunchCommand? command)
    {
        var path = dir.File(name);
        File.WriteAllText(path, "x");
        registry.Record(path);
        writer.Arguments[path] = command is null ? null : LaunchCommandTests.SplitLikeWindows(command.ToArgumentString());
        return path;
    }

    private sealed class FakeShortcutReader : IShortcutWriter
    {
        public Dictionary<string, string[]?> Arguments { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Write(ShortcutSpec spec) => throw new NotSupportedException();

        public IReadOnlyList<string>? TryReadArguments(string path) => Arguments.GetValueOrDefault(path);
    }
}
