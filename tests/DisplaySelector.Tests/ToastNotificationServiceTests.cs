using DisplaySelector.Core.Notifications;
using Xunit;

namespace DisplaySelector.Tests;

public class ToastNotificationServiceTests
{
    [Fact]
    public void Toasts_are_silent_so_the_confirmation_tone_is_the_only_sound()
    {
        var xml = ToastNotificationService.BuildXml("Switched to: TV");

        Assert.Contains("Switched to: TV", xml);
        Assert.Contains("<audio silent=\"true\"", xml);
    }
}
