namespace DisplaySelector.UI;

/// <summary>
/// The file chooser every "Create a Launcher…" flow starts with. Accepts programs plus existing
/// shortcuts — including the <c>.url</c> desktop shortcuts Steam/Epic create, which is how launcher URIs
/// (<c>steam://rungameid/…</c>) are picked without typing them.
/// </summary>
internal static class LauncherTargetPicker
{
    public static string? Pick(IWin32Window? owner)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the game or program to launch",
            Filter = "Programs and shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All files (*.*)|*.*",
            // Keep a picked .lnk/.url as-is so its own arguments/working folder/URI are honoured.
            DereferenceLinks = false,
            CheckFileExists = true,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
}
