using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisplaySelector.Core.Profiles;

/// <summary>
/// Durable JSON read/write. Writes go to a <c>*.tmp</c> sibling then atomically replace the target
/// via <see cref="File.Replace(string, string, string?)"/>, retaining the previous content as
/// <c>*.bak</c> so a crash mid-write cannot corrupt the live file.
/// </summary>
internal static class AtomicJsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string BackupPath(string path) => path + ".bak";

    public static void Write<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(value, Options);
        File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(path))
        {
            File.Replace(tmp, path, BackupPath(path));
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    /// <summary>Returns null if the file is missing; throws on malformed JSON (caller handles recovery).</summary>
    public static T? TryRead<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
