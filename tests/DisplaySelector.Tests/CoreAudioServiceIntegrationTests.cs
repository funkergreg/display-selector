using DisplaySelector.Core.Audio;
using Xunit;
using Xunit.Abstractions;

namespace DisplaySelector.Tests;

/// <summary>
/// Tier-2 integration: exercises the real Core Audio APIs, non-destructively. Excluded from the
/// default unit run (Category=Integration). Setting the default device targets the CURRENT default,
/// so it round-trips without actually changing the user's configuration. Tests tolerate a session
/// with no audio endpoints (headless agents) by no-opping rather than failing.
/// </summary>
[Trait("Category", "Integration")]
public class CoreAudioServiceIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CoreAudioServiceIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Enumerates_output_devices()
    {
        var service = new CoreAudioService(new NullLog());

        var devices = service.GetOutputDevices();
        foreach (var d in devices)
        {
            _output.WriteLine($"{(d.IsDefault ? "* " : "  ")}{d.FriendlyName}  [{d.Id}]");
        }

        Assert.NotNull(devices);
    }

    [Fact]
    public void Default_device_is_among_enumerated_devices()
    {
        var service = new CoreAudioService(new NullLog());

        var def = service.GetDefaultOutputDevice();
        if (def is null)
        {
            _output.WriteLine("No default render endpoint on this session; skipping.");
            return;
        }

        Assert.Contains(service.GetOutputDevices(), d => d.Id == def.Id);
    }

    [Fact]
    public void Setting_default_to_current_default_succeeds()
    {
        var service = new CoreAudioService(new NullLog());

        var def = service.GetDefaultOutputDevice();
        if (def is null)
        {
            _output.WriteLine("No default render endpoint on this session; skipping.");
            return;
        }

        // No-op switch: validates the IPolicyConfig path without altering the user's setup.
        Assert.True(service.SetDefaultOutputDevice(def.Id));
    }
}
