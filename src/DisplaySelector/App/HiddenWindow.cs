using DisplaySelector.Core.Interop;

namespace DisplaySelector.App;

/// <summary>
/// A hidden, top-level message window. Top-level (not message-only) so it can receive the
/// broadcast <c>RegisterWindowMessage</c> a second instance posts, and so a second instance can find it
/// by caption to forward a game shortcut's arguments (<see cref="CommandChannel"/>), and so it hears the
/// broadcast <c>WM_DISPLAYCHANGE</c> / <c>WM_DEVICECHANGE</c>. WS_EX_TOOLWINDOW + invisible keeps it out of Alt-Tab and the taskbar.
/// </summary>
internal sealed class HiddenWindow : NativeWindow, IDisposable
{
    private readonly uint _watchedMessage;

    public event Action? MessageReceived;

    /// <summary>Command-line arguments forwarded by a second launch. Raised on the UI thread, after the send returns.</summary>
    public event Action<string[]>? CommandReceived;

    /// <summary>
    /// The display layout changed (by anyone), or a device was plugged in or out (e.g. a display that
    /// isn't in use, which raises no display change). Raised on the UI thread; several arrive per switch.
    /// </summary>
    public event Action? HardwareChanged;

    public HiddenWindow(uint watchedMessage)
    {
        _watchedMessage = watchedMessage;
        var cp = new CreateParams
        {
            Caption = CommandChannel.ListenerCaption,
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
        else if (m.Msg == NativeMethods.WM_DISPLAYCHANGE
            || (m.Msg == NativeMethods.WM_DEVICECHANGE && m.WParam == NativeMethods.DBT_DEVNODES_CHANGED))
        {
            HardwareChanged?.Invoke();
        }
        else if (m.Msg == NativeMethods.WM_COPYDATA && CommandChannel.TryRead(m.LParam, out var args))
        {
            // Ack and return immediately so the sending process isn't blocked while we switch displays;
            // the work runs as a posted callback on this (UI) thread.
            m.Result = 1;
            var context = SynchronizationContext.Current;
            if (context is null)
            {
                CommandReceived?.Invoke(args);
            }
            else
            {
                context.Post(_ => CommandReceived?.Invoke(args), null);
            }
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
