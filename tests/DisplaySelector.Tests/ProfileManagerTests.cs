using DisplaySelector.App;
using DisplaySelector.Core.Launch;
using DisplaySelector.Core.Profiles;
using Xunit;

namespace DisplaySelector.Tests;

public class ProfileLabelsTests
{
    [Fact]
    public void Label_with_hotkey_matches_the_tray_menu()
    {
        var profile = new Profile { Name = "TV", Hotkey = new HotkeyBinding { Modifiers = { "Control", "Alt" }, Key = "F10" } };

        Assert.Equal("TV : Ctrl+Alt+F10", ProfileLabels.Label(profile));
    }

    [Fact]
    public void Label_without_hotkey_says_so()
    {
        Assert.Equal("Desk : No Hotkey", ProfileLabels.Label(new Profile { Name = "Desk" }));
    }
}

public class ProfileListSelectionTests
{
    [Fact]
    public void Keeps_the_selected_profile_by_id()
    {
        Assert.Equal("b", ProfileListSelection.Next(new[] { "a", "b", "c" }, "b", new[] { "a", "b", "c" }));
    }

    [Fact]
    public void Follows_a_moved_profile_so_move_can_repeat()
    {
        Assert.Equal("c", ProfileListSelection.Next(new[] { "a", "b", "c" }, "c", new[] { "a", "c", "b" }));
    }

    [Fact]
    public void After_delete_selects_the_same_position_clamped()
    {
        Assert.Equal("c", ProfileListSelection.Next(new[] { "a", "b", "c" }, "b", new[] { "a", "c" }));
        Assert.Equal("b", ProfileListSelection.Next(new[] { "a", "b", "c" }, "c", new[] { "a", "b" }));
    }

    [Fact]
    public void A_newly_saved_profile_is_selected()
    {
        Assert.Equal("d", ProfileListSelection.Next(new[] { "a", "b" }, "a", new[] { "a", "b", "d" }));
    }

    [Fact]
    public void Empty_list_selects_nothing_and_first_load_selects_the_first()
    {
        Assert.Null(ProfileListSelection.Next(new[] { "a" }, "a", Array.Empty<string>()));
        Assert.Equal("a", ProfileListSelection.Next(Array.Empty<string>(), null, new[] { "a", "b" }));
    }
}

public class LauncherSpecBuilderTests
{
    private static readonly Profile Tv = new() { Id = "0123456789abcdef0123456789abcdef", Name = "Living Room TV" };

    [Fact]
    public void Builds_a_desktop_lnk_that_runs_the_app_with_the_launch_command()
    {
        var spec = LauncherSpecBuilder.Build(
            Tv, @"C:\Games\My Game\game.exe", "-windowed", "game (Living Room TV)", @"C:\App\DisplaySelector.exe", @"C:\Users\me\Desktop");

        Assert.Equal(@"C:\Users\me\Desktop\game (Living Room TV).lnk", spec.Path);
        Assert.Equal(@"C:\App\DisplaySelector.exe", spec.TargetPath);
        Assert.Equal(@"C:\App", spec.WorkingDirectory);
        Assert.Contains("Living Room TV", spec.Description);
        Assert.Contains("game", spec.Description);
    }

    [Fact]
    public void Arguments_reference_the_profile_by_id_and_round_trip()
    {
        var spec = LauncherSpecBuilder.Build(
            Tv, @"C:\Games\My Game\game.exe", "-windowed \"x y\"", "n", @"C:\App\DisplaySelector.exe", @"C:\Desktop");

        var parsed = LaunchCommand.TryParse(LaunchCommandTests.SplitLikeWindows(spec.Arguments), out _);

        Assert.Equal(new LaunchCommand(Tv.Id, @"C:\Games\My Game\game.exe", "-windowed \"x y\""), parsed);
    }

    [Fact]
    public void Switch_only_launcher_has_no_launch_target_and_uses_the_app_icon()
    {
        var spec = LauncherSpecBuilder.Build(Tv, null, "ignored", "Living Room TV", @"C:\App\DisplaySelector.exe", @"C:\Desktop");

        var parsed = LaunchCommand.TryParse(LaunchCommandTests.SplitLikeWindows(spec.Arguments), out _);

        Assert.Equal(new LaunchCommand(Tv.Id), parsed); // arguments are meaningless without a target
        Assert.Equal(@"C:\Desktop\Living Room TV.lnk", spec.Path);
        Assert.Equal(@"C:\App\DisplaySelector.exe", spec.IconPath);
        Assert.DoesNotContain("launch", spec.Description);
    }

    [Fact]
    public void Icon_is_the_target_exe_when_it_exists_otherwise_the_app()
    {
        var existingExe = Environment.ProcessPath!; // the test host — a real .exe on disk
        var withExe = LauncherSpecBuilder.Build(Tv, existingExe, null, "n", @"C:\App\DisplaySelector.exe", @"C:\Desktop");
        var withUrl = LauncherSpecBuilder.Build(Tv, @"C:\Users\me\Desktop\Game.url", null, "n", @"C:\App\DisplaySelector.exe", @"C:\Desktop");

        Assert.Equal(existingExe, withExe.IconPath);
        Assert.Equal(@"C:\App\DisplaySelector.exe", withUrl.IconPath);
    }
}
