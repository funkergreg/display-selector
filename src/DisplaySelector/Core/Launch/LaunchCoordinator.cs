using DisplaySelector.Core.Activation;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Launch;

/// <summary>What happened while running a <see cref="LaunchCommand"/>; the controller turns it into a toast.</summary>
public sealed record LaunchOutcome(
    LaunchCommand Command,
    Profile? Profile,
    ActivationResult? Activation,
    SettleResult Settle,
    bool Launched,
    string? LaunchError)
{
    public bool LaunchAttempted => Command.Target is not null;

    /// <summary>Displays that were off at activation but connected during the wait, so the profile was re-applied.</summary>
    public IReadOnlyList<DisplayTarget> ReconnectedDisplays { get; init; } = Array.Empty<DisplayTarget>();
}

/// <summary>
/// Runs a game-shortcut command: resolve profile → activate → wait for displays to settle → launch.
/// When the profile is already live, activation applies nothing and the launch is immediate.
/// WinForms-free (activation is passed in as a delegate so the controller keeps its toast/tray path),
/// so the ordering and failure rules are unit-tested. The game always launches: a missing profile,
/// failed activation, or settle timeout is reported, never a blocker. Cancellation (a newer command
/// superseding this one) throws <see cref="OperationCanceledException"/> before the launch.
/// <para>
/// If a profile display isn't connected yet (the TV is still off), the wait holds the launch open; when
/// it connects the profile is activated again, so turning the TV on just after the shortcut still
/// lands the game on it. Each display can trigger that once, so a flapping HDMI link can't loop.
/// </para>
/// </summary>
public sealed class LaunchCoordinator
{
    private readonly IProcessLauncher _launcher;
    private readonly DisplaySettleWaiter _settle;
    private readonly ILog _log;

    public LaunchCoordinator(IProcessLauncher launcher, DisplaySettleWaiter settle, ILog log)
    {
        _launcher = launcher;
        _settle = settle;
        _log = log;
    }

    public async Task<LaunchOutcome> RunAsync(
        LaunchCommand command,
        IReadOnlyList<Profile> profiles,
        Func<Profile, ActivationResult?> activate,
        CancellationToken cancellationToken)
    {
        var profile = ProfileResolver.Resolve(profiles, command.ProfileRef);
        ActivationResult? activation = null;
        var settle = SettleResult.Skipped;
        var reconnected = new List<DisplayTarget>();

        if (profile is null)
        {
            _log.Info($"Launch command refers to unknown profile '{command.ProfileRef}'; not switching.");
        }
        else
        {
            // Only wait when there is something to launch and a layout is being applied. The baseline
            // lets the wait recognise "it changed" when a full match is impossible.
            var waitForDisplays = command.Target is not null && profile.Display is not null;
            var baseline = waitForDisplays ? _settle.CaptureBaseline() : null;

            activation = activate(profile);

            // Already live: nothing changed, so there's nothing to wait for; launch straight away.
            while (waitForDisplays && activation is { AlreadyActive: false })
            {
                // Displays still to wait for: missing now, and not already re-applied for once.
                var missing = activation.UnavailableDisplays
                    .Where(t => reconnected.All(r => r.StableId != t.StableId))
                    .ToList();

                // A failed apply is only worth waiting on if a missing display might fix it.
                if (!activation.Success && missing.Count == 0)
                {
                    break;
                }

                settle = await _settle.WaitAsync(
                    profile.Display!, baseline, missing.Select(t => t.StableId).ToList(), cancellationToken);
                if (settle.Outcome != SettleOutcome.DisplayConnected)
                {
                    break;
                }

                reconnected.AddRange(missing.Where(t => settle.ConnectedDisplayIds.Contains(t.StableId)));
                _log.Info($"Re-activating '{profile.Name}': {string.Join(", ", reconnected.Select(t => t.Friendly))} connected.");
                var again = activate(profile);
                if (again is null)
                {
                    break;
                }

                if (again.AlreadyActive)
                {
                    // Windows brought the profile fully live when the display connected: report it as applied
                    // (the first result would still list that display as unavailable).
                    activation = new ActivationResult(true, Array.Empty<string>());
                    break;
                }

                activation = again;
            }
        }

        if (command.Target is null)
        {
            return new LaunchOutcome(command, profile, activation, settle, Launched: false, LaunchError: null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var launched = _launcher.TryLaunch(command.Target, command.Arguments, out var error);
        _log.Info(launched
            ? $"Launched '{command.Target}'."
            : $"Launch of '{command.Target}' failed: {error}");
        return new LaunchOutcome(command, profile, activation, settle, launched, error)
        {
            ReconnectedDisplays = reconnected,
        };
    }
}
