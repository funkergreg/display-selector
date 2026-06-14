namespace DisplaySelector.Core;

/// <summary>
/// Single source of truth for the app's identity. The <see cref="AppUserModelId"/> must match the
/// Start Menu shortcut's AUMID created by the installer (and, for a future MSIX build, the package
/// manifest's Application Id) so toast notifications attribute correctly.
/// See docs/microsoft-store-distribution-roadmap.md.
/// </summary>
public static class AppIdentity
{
    public const string AppName = "Display Selector";

    /// <summary>AppUserModelID — keep in sync with installer/setup.iss and any future MSIX manifest.</summary>
    public const string AppUserModelId = "FunkerGreg.DisplaySelector";

    /// <summary>Proposed MSIX package identity name (Partner Center assigns the final Publisher CN). Documented for later.</summary>
    public const string MsixPackageName = "FunkerGreg.DisplaySelector";

    public const string ProjectUrl = "https://github.com/funkergreg/display-selector";
}
