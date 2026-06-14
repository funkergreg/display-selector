using System.Runtime.InteropServices;
using System.Text;

namespace DisplaySelector.Core;

/// <summary>
/// Detects whether the process is running inside an MSIX package. Lets platform code pick the right
/// implementation — e.g. auto-start uses a StartupTask under MSIX (the HKCU Run key is virtualized
/// and ineffective there). Currently always false for our unpackaged builds; it's the seam for a
/// future MSIX target (see docs/microsoft-store-distribution-roadmap.md).
/// </summary>
public static class PackageContext
{
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged { get; } = DetectPackaged();

    private static bool DetectPackaged()
    {
        var length = 0;
        var rc = GetCurrentPackageFullName(ref length, null);
        return rc != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
