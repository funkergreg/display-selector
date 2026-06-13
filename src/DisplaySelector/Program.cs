using DisplaySelector.App;
using DisplaySelector.Core;
using DisplaySelector.Core.Audio;
using DisplaySelector.Core.Display;
using DisplaySelector.Core.Interop;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector;

internal static class Program
{
    private const string MutexName = @"Local\DisplaySelector.SingleInstance";

    private static readonly uint SurfaceMessage =
        NativeMethods.RegisterWindowMessageW("DisplaySelector_ShowFirstInstance");

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance owns the tray — ask it to surface its menu, then exit.
            NativeMethods.PostMessageW(NativeMethods.HWND_BROADCAST, SurfaceMessage, IntPtr.Zero, IntPtr.Zero);
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
            using var context = new TrayApplicationContext(logger, profileStore, configStore, audioService, displayService, SurfaceMessage);
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
