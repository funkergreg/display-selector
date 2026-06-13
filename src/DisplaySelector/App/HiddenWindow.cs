using DisplaySelector.Core.Interop;

namespace DisplaySelector.App;

/// <summary>
/// A hidden, top-level message window. Top-level (not message-only) so it can receive the
/// broadcast <c>RegisterWindowMessage</c> a second instance posts; WS_EX_TOOLWINDOW + invisible
/// keeps it out of Alt-Tab and the taskbar. (In M3 the hotkey service will use its own window.)
/// </summary>
internal sealed class HiddenWindow : NativeWindow, IDisposable
{
    private readonly uint _watchedMessage;

    public event Action? MessageReceived;

    public HiddenWindow(uint watchedMessage)
    {
        _watchedMessage = watchedMessage;
        var cp = new CreateParams
        {
            Caption = "DisplaySelectorListener",
            ExStyle = NativeMethods.WS_EX_TOOLWINDOW,
        };
        CreateHandle(cp);
    }

    protected override void WndProc(ref Message m)
    {
        if ((uint)m.Msg == _watchedMessage)
        {
            MessageReceived?.Invoke();
        }

        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
