namespace DisplaySelector.UI;

/// <summary>
/// App-wide rule: a window opened from the tray (or the Manage profiles window) exists at most once.
/// Re-invoking its command brings the open window to the front (restored if minimized) instead of
/// stacking a second copy — the tray menu stays clickable even while a modal window is up.
/// </summary>
internal sealed class SingleInstanceWindow<T>
    where T : Form
{
    private T? _current;

    /// <summary>The open window, or null.</summary>
    public T? Current => _current is { IsDisposed: false } open ? open : null;

    public void ShowOrActivate(Func<T> create, bool modal, Action? onClosed = null)
    {
        if (Current is { } open)
        {
            if (open.WindowState == FormWindowState.Minimized)
            {
                open.WindowState = FormWindowState.Normal;
            }
            open.Activate();
            return;
        }

        var form = create();
        _current = form;
        form.FormClosed += (_, _) =>
        {
            _current = null;
            onClosed?.Invoke();
        };

        if (modal)
        {
            try
            {
                form.ShowDialog();
            }
            finally
            {
                form.Dispose();
            }
        }
        else
        {
            form.Show(); // a modeless form disposes itself when closed
        }
    }

    public void Close() => Current?.Close();
}
