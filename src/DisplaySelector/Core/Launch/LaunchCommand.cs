using System.Text;

namespace DisplaySelector.Core.Launch;

/// <summary>
/// A "switch to a profile, then optionally launch something" request, carried on the command line of a
/// game shortcut: <c>DisplaySelector.exe --profile &lt;id-or-name&gt; [--launch "&lt;target&gt;"] [--args "&lt;arguments&gt;"]</c>.
/// WinForms-free and pure so the contract is unit-tested; shortcuts in the wild depend on it staying stable.
/// </summary>
public sealed record LaunchCommand(string ProfileRef, string? Target = null, string? Arguments = null)
{
    public const string ProfileFlag = "--profile";
    public const string LaunchFlag = "--launch";
    public const string ArgsFlag = "--args";

    /// <summary>
    /// Parses process arguments. Returns null when there is no usable <c>--profile</c> (including no
    /// arguments at all — a plain launch). Unknown tokens are reported in <paramref name="ignored"/> and
    /// otherwise skipped, so shortcuts written by a newer version still work on this one.
    /// </summary>
    public static LaunchCommand? TryParse(IReadOnlyList<string> args, out IReadOnlyList<string> ignored)
    {
        var skipped = new List<string>();
        ignored = skipped;

        string? profile = null;
        string? target = null;
        string? arguments = null;

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];

            // Value-taking flags consume the next token unconditionally, so "--args --fullscreen" works.
            if (IsFlag(token, ProfileFlag) && i + 1 < args.Count)
            {
                profile = args[++i];
            }
            else if (IsFlag(token, LaunchFlag) && i + 1 < args.Count)
            {
                target = args[++i];
            }
            else if (IsFlag(token, ArgsFlag) && i + 1 < args.Count)
            {
                arguments = args[++i];
            }
            else
            {
                skipped.Add(token);
            }
        }

        if (string.IsNullOrWhiteSpace(profile))
        {
            return null;
        }

        return new LaunchCommand(
            profile.Trim(),
            string.IsNullOrWhiteSpace(target) ? null : target.Trim(),
            string.IsNullOrWhiteSpace(arguments) ? null : arguments);
    }

    /// <summary>The argument string for a shortcut's Arguments field (quoted per CommandLineToArgvW rules).</summary>
    public string ToArgumentString()
    {
        var parts = new List<string> { ProfileFlag, Quote(ProfileRef) };
        if (Target is not null)
        {
            parts.Add(LaunchFlag);
            parts.Add(Quote(Target));
        }
        if (Arguments is not null)
        {
            parts.Add(ArgsFlag);
            parts.Add(Quote(Arguments));
        }

        return string.Join(' ', parts);
    }

    /// <summary>A short, human-readable name for the launch target (file name, or the URI as-is).</summary>
    public string? TargetDisplayName => Target is null ? null : DisplayNameOf(Target);

    /// <summary>True for a launcher link (e.g. <c>steam://rungameid/…</c>) rather than a file path.</summary>
    public static bool IsUri(string target) => target.Contains("://", StringComparison.Ordinal);

    public static string DisplayNameOf(string target)
    {
        if (IsUri(target))
        {
            return target;
        }

        var name = Path.GetFileNameWithoutExtension(target.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? target : name;
    }

    private static bool IsFlag(string token, string flag) => string.Equals(token, flag, StringComparison.OrdinalIgnoreCase);

    // Quotes one argument so CommandLineToArgvW (and .NET's own parser) reads it back verbatim:
    // backslashes are literal except when they precede a quote, where they must be doubled.
    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
        {
            return arg;
        }

        var sb = new StringBuilder("\"");
        for (var i = 0; ; i++)
        {
            var backslashes = 0;
            while (i < arg.Length && arg[i] == '\\')
            {
                i++;
                backslashes++;
            }

            if (i == arg.Length)
            {
                sb.Append('\\', backslashes * 2);
                break;
            }

            if (arg[i] == '"')
            {
                sb.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(arg[i]);
            }
        }

        return sb.Append('"').ToString();
    }
}
