using System.Runtime.InteropServices;
using DisplaySelector.Core.Audio.Interop;
using DisplaySelector.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DisplaySelector.Core.Audio;

/// <summary>
/// Core Audio implementation: NAudio for enumeration + WASAPI playback; the undocumented
/// <see cref="IPolicyConfig"/> for setting the default endpoint across all roles.
/// </summary>
public sealed class CoreAudioService : IAudioService
{
    private const int ToneFrequencyHz = 880;
    private static readonly TimeSpan ToneDuration = TimeSpan.FromMilliseconds(220);

    private readonly ILog _log;

    public CoreAudioService(ILog log) => _log = log;

    public IReadOnlyList<AudioEndpoint> GetOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = TryGetDefaultId(enumerator);

        var result = new List<AudioEndpoint>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                result.Add(new AudioEndpoint(device.ID, device.FriendlyName, device.ID == defaultId));
            }
        }

        return result;
    }

    public AudioEndpoint? GetDefaultOutputDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            return null;
        }

        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return new AudioEndpoint(device.ID, device.FriendlyName, true);
    }

    public bool SetDefaultOutputDevice(string endpointId)
    {
        IPolicyConfig? client = null;
        try
        {
            client = PolicyConfig.CreateClient();
            foreach (var role in PolicyConfig.AllRoles)
            {
                var hr = client.SetDefaultEndpoint(endpointId, role);
                if (hr != 0)
                {
                    _log.Error($"SetDefaultEndpoint failed for role {role} (hr=0x{hr:X8}) on {endpointId}");
                    return false;
                }
            }

            _log.Info($"Set default output device for all roles: {endpointId}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"SetDefaultOutputDevice threw for {endpointId}.", ex);
            return false;
        }
        finally
        {
            if (client is not null)
            {
                Marshal.FinalReleaseComObject(client);
            }
        }
    }

    public void PlayConfirmation(string? endpointId = null)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = ResolveDevice(enumerator, endpointId);
        try
        {
            var signal = new SignalGenerator(44100, 1)
            {
                Gain = 0.2,
                Frequency = ToneFrequencyHz,
                Type = SignalGeneratorType.Sin,
            };
            var tone = new OffsetSampleProvider(signal) { Take = ToneDuration };

            using var output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: false, latency: 100);
            output.Init(tone);
            output.Play();
            while (output.PlaybackState == PlaybackState.Playing)
            {
                Thread.Sleep(20);
            }

            _log.Info($"Played confirmation tone on '{device.FriendlyName}'.");
        }
        catch (Exception ex)
        {
            _log.Error("PlayConfirmation failed.", ex);
        }
        finally
        {
            device.Dispose();
        }
    }

    private static MMDevice ResolveDevice(MMDeviceEnumerator enumerator, string? endpointId)
    {
        if (endpointId is not null)
        {
            try
            {
                return enumerator.GetDevice(endpointId);
            }
            catch
            {
                // Fall through to default if the requested endpoint is gone.
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator)
    {
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            return null;
        }

        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return device.ID;
    }
}
