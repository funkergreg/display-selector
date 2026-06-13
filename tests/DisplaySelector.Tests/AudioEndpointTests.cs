using DisplaySelector.Core.Audio;
using Xunit;

namespace DisplaySelector.Tests;

public class AudioEndpointTests
{
    [Fact]
    public void Records_compare_by_value()
    {
        var a = new AudioEndpoint("{id}", "Speakers", true);
        var b = new AudioEndpoint("{id}", "Speakers", true);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Distinct_ids_are_not_equal()
    {
        var a = new AudioEndpoint("{id-1}", "Speakers", false);
        var b = new AudioEndpoint("{id-2}", "Speakers", false);

        Assert.NotEqual(a, b);
    }
}
