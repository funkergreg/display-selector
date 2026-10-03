using DisplaySelector.Core.Launch;
using Xunit;

namespace DisplaySelector.Tests;

public class LaunchCommandTests
{
    [Fact]
    public void No_arguments_is_a_plain_launch()
    {
        Assert.Null(LaunchCommand.TryParse(Array.Empty<string>(), out var ignored));
        Assert.Empty(ignored);
    }

    [Fact]
    public void Parses_profile_target_and_arguments()
    {
        var command = LaunchCommand.TryParse(
            new[] { "--profile", "abc123", "--launch", @"C:\Games\Game.exe", "--args", "-windowed -w 1920" },
            out var ignored);

        Assert.Equal(new LaunchCommand("abc123", @"C:\Games\Game.exe", "-windowed -w 1920"), command);
        Assert.Empty(ignored);
    }

    [Fact]
    public void Profile_only_is_a_switch_only_command()
    {
        var command = LaunchCommand.TryParse(new[] { "--profile", "TV" }, out _);

        Assert.Equal(new LaunchCommand("TV"), command);
        Assert.Null(command!.Target);
    }

    [Fact]
    public void Flags_are_case_insensitive()
    {
        var command = LaunchCommand.TryParse(new[] { "--PROFILE", "TV", "--Launch", "steam://rungameid/570" }, out _);

        Assert.Equal(new LaunchCommand("TV", "steam://rungameid/570"), command);
    }

    [Fact]
    public void Value_flags_take_the_next_token_even_if_it_looks_like_a_flag()
    {
        var command = LaunchCommand.TryParse(new[] { "--profile", "TV", "--launch", "game.exe", "--args", "--fullscreen" }, out _);

        Assert.Equal("--fullscreen", command!.Arguments);
    }

    [Fact]
    public void Unknown_tokens_are_ignored_and_reported()
    {
        // A shortcut written by a newer version (e.g. a future --revert flag) still works here.
        var command = LaunchCommand.TryParse(new[] { "--revert", "--profile", "TV", "stray" }, out var ignored);

        Assert.Equal(new LaunchCommand("TV"), command);
        Assert.Equal(new[] { "--revert", "stray" }, ignored);
    }

    // Arguments are '|'-separated (attribute arguments can't be string arrays).
    [Theory]
    [InlineData("--launch|game.exe")]
    [InlineData("--profile")]
    [InlineData("--profile|   ")]
    [InlineData("garbage")]
    public void Missing_profile_is_not_a_command(string args)
    {
        Assert.Null(LaunchCommand.TryParse(args.Split('|'), out _));
    }

    [Fact]
    public void Blank_target_and_arguments_become_null()
    {
        var command = LaunchCommand.TryParse(new[] { "--profile", "TV", "--launch", " ", "--args", "" }, out _);

        Assert.Equal(new LaunchCommand("TV"), command);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", null, null)]
    [InlineData("Living Room TV", @"C:\Program Files (x86)\Steam\steamapps\common\My Game\game.exe", null)]
    [InlineData("TV", "steam://rungameid/570", null)]
    [InlineData("TV", @"C:\Games\Game.exe", "-windowed \"C:\\Saves\\slot 1\" -name=\"Ünïcödé 名前\"")]
    [InlineData("TV", @"\\server\share\dir with space\", @"trailing backslash\")]
    [InlineData("quote\"inside", @"C:\a\\b\", "\\\"already\\\" escaped")]
    public void ToArgumentString_round_trips_through_the_windows_parser(string profile, string? target, string? arguments)
    {
        var original = new LaunchCommand(profile, target, arguments);

        var argv = SplitLikeWindows(original.ToArgumentString());
        var parsed = LaunchCommand.TryParse(argv, out var ignored);

        Assert.Empty(ignored);
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData(@"C:\Games\Witcher 3\witcher3.exe", "witcher3")]
    [InlineData(@"C:\Users\me\Desktop\Elden Ring.lnk", "Elden Ring")]
    [InlineData("steam://rungameid/570", "steam://rungameid/570")]
    public void TargetDisplayName_is_short_and_readable(string target, string expected)
    {
        Assert.Equal(expected, new LaunchCommand("TV", target).TargetDisplayName);
    }

    // Splits exactly as the OS hands arguments to a process (CommandLineToArgvW), dropping argv[0].
    internal static string[] SplitLikeWindows(string arguments) => ShellLinkShortcutWriter.SplitArguments(arguments);
}
