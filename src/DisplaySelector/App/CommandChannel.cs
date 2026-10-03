using System.Runtime.InteropServices;
using DisplaySelector.Core.Interop;

namespace DisplaySelector.App;

/// <summary>
/// Forwards a second launch's command-line arguments (a game shortcut) to the running instance via
/// <c>WM_COPYDATA</c> to its <see cref="HiddenWindow"/>. Payload = the raw args joined with NUL (args
/// can't contain NUL), so the tray parses and logs exactly what the shortcut passed. Same-session,
/// same-or-higher-integrity senders only (UIPI); tagged and size-capped on receipt.
/// </summary>
internal static class CommandChannel
{
    public const string ListenerCaption = "DisplaySelectorListener";

    // Identifies our payload in COPYDATASTRUCT.dwData ("DSCM").
    private const uint Tag = 0x4D435344;

    // A Windows command line tops out at 32,767 UTF-16 chars.
    private const int MaxPayloadBytes = 32_767 * 2;

    private const uint SendTimeoutMs = 5000;

    // The mutex owner may still be starting up (e.g. two shortcuts double-clicked in quick succession),
    // so keep looking for its listener window for a few seconds before giving up.
    private const int FindTimeoutMs = 5000;
    private const int FindRetryMs = 100;

    /// <summary>Sends <paramref name="args"/> to the running instance; false if it couldn't be reached.</summary>
    public static unsafe bool TrySend(IReadOnlyList<string> args)
    {
        var deadline = Environment.TickCount64 + FindTimeoutMs;
        IntPtr target;
        while ((target = NativeMethods.FindWindowW(null, ListenerCaption)) == IntPtr.Zero)
        {
            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }
            Thread.Sleep(FindRetryMs);
        }

        var payload = string.Join('\0', args);
        if (payload.Length * 2 > MaxPayloadBytes)
        {
            return false;
        }

        fixed (char* data = payload)
        {
            var cds = new NativeMethods.COPYDATASTRUCT
            {
                dwData = Tag,
                cbData = payload.Length * 2,
                lpData = (IntPtr)data,
            };

            // Timeout + abort-if-hung so a stuck tray can never hang the shortcut.
            var sent = NativeMethods.SendMessageTimeoutW(
                target,
                NativeMethods.WM_COPYDATA,
                IntPtr.Zero,
                (IntPtr)(&cds),
                NativeMethods.SMTO_ABORTIFHUNG,
                SendTimeoutMs,
                out var result);
            return sent != IntPtr.Zero && result != IntPtr.Zero;
        }
    }

    /// <summary>Decodes a received <c>WM_COPYDATA</c>; false if it isn't ours or is malformed.</summary>
    public static bool TryRead(IntPtr lParam, out string[] args)
    {
        args = Array.Empty<string>();
        if (lParam == IntPtr.Zero)
        {
            return false;
        }

        var cds = Marshal.PtrToStructure<NativeMethods.COPYDATASTRUCT>(lParam);
        if (cds.dwData != Tag || cds.cbData <= 0 || cds.cbData > MaxPayloadBytes || cds.cbData % 2 != 0 || cds.lpData == IntPtr.Zero)
        {
            return false;
        }

        // Copy out now: the sender's buffer is only valid for the duration of the message.
        var payload = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2);
        args = payload.Split('\0');
        return true;
    }
}
