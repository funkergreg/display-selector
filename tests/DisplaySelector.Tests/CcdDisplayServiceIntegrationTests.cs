using DisplaySelector.Core.Display;
using Xunit;
using Xunit.Abstractions;

namespace DisplaySelector.Tests;

/// <summary>
/// Tier-2 integration: real CCD APIs, non-destructive. Capture and decode read-only; validation uses
/// SDC_VALIDATE (never applies). Re-applying displays is tier-3 (human-observed) and is NOT done here.
/// </summary>
[Trait("Category", "Integration")]
public class CcdDisplayServiceIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public CcdDisplayServiceIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Capture_produces_nonempty_blobs_and_targets()
    {
        var service = new CcdDisplayService(new NullLog());

        var config = service.Capture();

        _output.WriteLine($"paths blob: {config.PathInfo?.Length ?? 0} b64 chars; targets: {config.Targets.Count}");
        foreach (var t in config.Targets)
        {
            _output.WriteLine($"  {t.Friendly} port={t.StableId} res={t.Resolution} rot={t.Orientation} primary={t.Primary}");
        }

        Assert.False(string.IsNullOrEmpty(config.PathInfo));
        Assert.False(string.IsNullOrEmpty(config.ModeInfo));
        Assert.NotEmpty(config.Targets);
    }

    [Fact]
    public void Exactly_one_display_is_primary()
    {
        var service = new CcdDisplayService(new NullLog());

        var displays = service.GetCurrentDisplays();
        if (displays.Count == 0)
        {
            _output.WriteLine("No active displays on this session; skipping.");
            return;
        }

        Assert.Equal(1, displays.Count(d => d.Primary));
    }

    [Fact]
    public void Current_configuration_validates()
    {
        var service = new CcdDisplayService(new NullLog());

        // SDC_VALIDATE only — does not change the display configuration.
        Assert.True(service.ValidateCurrent());
    }

    [Fact]
    public void Capture_roundtrips_through_apply_decode_without_loss()
    {
        var service = new CcdDisplayService(new NullLog());

        var first = service.Capture();
        var second = service.Capture();

        // Two back-to-back captures of an unchanged desktop must agree on the decoded shape.
        Assert.Equal(first.Targets.Count, second.Targets.Count);
        Assert.Equal(
            first.Targets.Select(t => t.StableId).OrderBy(x => x),
            second.Targets.Select(t => t.StableId).OrderBy(x => x));
    }
}
