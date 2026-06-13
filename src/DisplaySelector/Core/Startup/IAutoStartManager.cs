namespace DisplaySelector.Core.Startup;

/// <summary>Manages whether the app launches at login. Implemented over the per-user HKCU Run key.</summary>
public interface IAutoStartManager
{
    bool IsEnabled();

    void Enable();

    void Disable();
}
