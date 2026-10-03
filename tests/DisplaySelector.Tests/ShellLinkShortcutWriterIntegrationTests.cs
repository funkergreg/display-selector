using DisplaySelector.Core.Launch;
using Xunit;

namespace DisplaySelector.Tests;

/// <summary>Writes a real .lnk through the shell COM objects (into a temp folder) and reads it back.</summary>
[Trait("Category", "Integration")]
public class ShellLinkShortcutWriterIntegrationTests
{
    [Fact]
    public void Writes_a_shortcut_that_reads_back_with_the_same_target_and_arguments()
    {
        using var dir = new TempDir();
        var path = dir.File("Game (TV).lnk");
        var exe = Environment.ProcessPath!;
        var arguments = new LaunchCommand("0123456789abcdef", @"C:\Games\My Game\game.exe", "-windowed").ToArgumentString();

        new ShellLinkShortcutWriter(new NullLog()).Write(new ShortcutSpec(
            path, exe, arguments, Path.GetDirectoryName(exe), "Switch to TV and launch game", exe));

        Assert.True(File.Exists(path));
        var (target, readArguments, description) = ShellLinkShortcutWriter.Read(path);
        Assert.Equal(exe, target, ignoreCase: true);
        Assert.Equal(arguments, readArguments);
        Assert.Equal("Switch to TV and launch game", description);
    }
}
