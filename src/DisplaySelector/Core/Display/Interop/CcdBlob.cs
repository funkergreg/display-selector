using System.Runtime.InteropServices;

namespace DisplaySelector.Core.Display.Interop;

/// <summary>
/// Converts blittable CCD struct arrays to/from base64 for persistence. The raw path/mode arrays are
/// stored verbatim so a profile restores resolution + orientation "for free" (see DESIGN/CLAUDE notes).
/// </summary>
internal static class CcdBlob
{
    public static string Encode<T>(T[] array)
        where T : unmanaged
        => Convert.ToBase64String(MemoryMarshal.AsBytes<T>(array));

    public static T[] Decode<T>(string base64)
        where T : unmanaged
    {
        var bytes = Convert.FromBase64String(base64);
        var array = new T[bytes.Length / Marshal.SizeOf<T>()];
        bytes.AsSpan().CopyTo(MemoryMarshal.AsBytes(array.AsSpan()));
        return array;
    }
}
