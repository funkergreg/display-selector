using DisplaySelector.App;
using DisplaySelector.Core;
using DisplaySelector.Core.Activation;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Hotkeys;
using DisplaySelector.Core.Interop;
using DisplaySelector.Core.Launch;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;
using DisplaySelector.Core.Startup;

namespace DisplaySelector;

internal static class Program
{
    private const string MutexName = @"Local\DisplaySelector.SingleInstance";

    private static readonly uint SurfaceMessage =
        NativeMethods.RegisterWindowMessageW("DisplaySelector_ShowFirstInstance");

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance owns the tray. A game shortcut forwards its arguments for the tray to run;
            // a plain launch (or a failed forward) asks it to surface its menu. Then exit.
            if (args.Length == 0 || !CommandChannel.TrySend(args))
            {
                NativeMethods.PostMessageW(NativeMethods.HWND_BROADCAST, SurfaceMessage, IntPtr.Zero, IntPtr.Zero);
            }
            return;
        }

        ApplicationConfiguration.Initialize();

        Directory.CreateDirectory(AppPaths.DataDirectory);
        var logger = new FileLogger(AppPaths.LogsDirectory);

        try
        {
            var profileStore = new JsonProfileStore(AppPaths.ProfilesFile, logger);
            var configStore = new JsonConfigStore(AppPaths.ConfigFile, logger);
            var audioService = new CoreAudioService(logger);
            var displayService = new CcdDisplayService(logger);
            var activator = new ProfileActivator(displayService, audioService, logger);
            var autoStart = new RunKeyAutoStart(logger);
            var launchCoordinator = new LaunchCoordinator(
                new ShellProcessLauncher(logger), new DisplaySettleWaiter(displayService, logger), logger);
            var shortcutWriter = new ShellLinkShortcutWriter(logger);
            var shortcutRegistry = new ShortcutRegistry(AppPaths.ShortcutsFile, logger);
            using var hotkeyService = new HotkeyService(logger);
            using var context = new TrayApplicationContext(
                logger, profileStore, configStore, audioService, displayService, hotkeyService, activator, autoStart,
                launchCoordinator, shortcutWriter, shortcutRegistry, SurfaceMessage, args);
            Application.Run(context);
        }
        catch (Exception ex)
        {
            logger.Error("Fatal error; application terminating.", ex);
            throw;
        }
        finally
        {
            GC.KeepAlive(mutex);
        }
    }
}
