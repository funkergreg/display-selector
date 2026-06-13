using DisplaySelector.Core.Interop;
using DisplaySelector.Core.Logging;
using DisplaySelector.Core.Profiles;

namespace DisplaySelector.Core.Hotkeys;

/// <summary>
/// RegisterHotKey-based implementation. Hosts a message-only window to receive WM_HOTKEY; must be
/// constructed on the UI thread (the one running the message loop).
/// </summary>
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private readonly ILog _log;
    private readonly HotkeyWindow _window;
    private readonly HashSet<int> _registered = new();

    public event Action<int>? HotkeyPressed;

    public HotkeyService(ILog log)
    {
        _log = log;
        _window = new HotkeyWindow();
        _window.HotkeyPressed += id => HotkeyPressed?.Invoke(id);
    }

    public bool TryRegister(int id, HotkeyBinding binding)
    {
        if (!HotkeyCodec.TryParse(binding, out var modifiers, out var virtualKey))
        {
            _log.Error($"Cannot parse hotkey '{HotkeyCodec.Format(binding)}'.");
            return false;
        }

        Unregister(id);

        if (!NativeMethods.RegisterHotKey(_window.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            _log.Info($"RegisterHotKey failed for '{HotkeyCodec.Format(binding)}' (likely already in use by another app).");
            return false;
        }

        _registered.Add(id);
        _log.Info($"Registered hotkey '{HotkeyCodec.Format(binding)}' (id={id}).");
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
        {
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.ToList())
        {
            Unregister(id);
        }
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Dispose();
    }

    private sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action<int>? HotkeyPressed;

        public HotkeyWindow()
        {
            // Message-only window (HWND_MESSAGE = -3): WM_HOTKEY is posted directly to it, not broadcast.
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                HotkeyPressed?.Invoke(m.WParam.ToInt32());
            }

            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }
}
