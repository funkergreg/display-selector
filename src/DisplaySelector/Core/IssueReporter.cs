using System.Text.RegularExpressions;

namespace DisplaySelector.Core;

/// <summary>
/// Builds prefilled GitHub "new issue" URLs (bug report / feature request) and prepares the
/// supporting payload. GitHub's new-issue URL can prefill <c>title</c>/<c>labels</c>/<c>body</c>
/// via query params but <b>cannot attach files</b>, and the whole URL must stay well under the
/// ~8&#160;KB practical cap — so the system profile goes inline in the body (truncated if needed)
/// while the (larger) log is handed off via the clipboard for the reporter to paste/attach.
/// Usernames are redacted because issues are public.
/// </summary>
public static class IssueReporter
{
    // Keep comfortably below GitHub's ~8 KB URL limit; the diagnostics block is trimmed to fit.
    private const int MaxUrlLength = 7000;

    /// <summary>Recent-log characters copied to the clipboard for pasting into the report.</summary>
    private const int LogTailChars = 6000;

    private static readonly string NewIssueUrl = $"{AppIdentity.ProjectUrl}/issues/new";

    /// <summary>
    /// Prefilled bug-report URL. Both the (already redacted) diagnostics and a short recent-log
    /// tail are inlined so the issue carries runtime context even if the reporter never pastes the
    /// full log from the clipboard. The log tail is pre-capped; the diagnostics block is trimmed
    /// last if the encoded URL would exceed the cap.
    /// </summary>
    public static string BugReportUrl(string diagnostics, string logTail)
    {
        var diag = diagnostics;
        var url = Build("bug", "[BUG] ", BugBody(diag, logTail));

        // Trim the diagnostics block until the encoded URL fits, then mark it truncated.
        if (url.Length > MaxUrlLength)
        {
            while (diag.Length > 0 && Build("bug", "[BUG] ", BugBody(diag, logTail)).Length > MaxUrlLength)
            {
                diag = diag[..Math.Max(0, diag.Length - 500)];
            }

            diag = diag.TrimEnd() + "\n…(diagnostics truncated — full output via Diagnostics ▸ Copy diagnostics)";
            url = Build("bug", "[BUG] ", BugBody(diag, logTail));
        }

        return url;
    }

    /// <summary>Prefilled feature-request URL (no diagnostics needed).</summary>
    public static string FeatureRequestUrl() => Build("enhancement", "[FEATURE] ", FeatureBody());

    /// <summary>
    /// Tail of the current log file (redacted), for the clipboard. Reads with a shared handle so it
    /// doesn't fight the live logger, and never throws — returns a placeholder on any failure.
    /// </summary>
    public static string ReadRecentLog()
    {
        try
        {
            var path = AppPaths.CurrentLogFile;
            if (!File.Exists(path))
            {
                return "(no log file yet)";
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            if (text.Length > LogTailChars)
            {
                text = "…(earlier log lines trimmed)\n" + text[^LogTailChars..];
            }

            return ScrubUser(text);
        }
        catch
        {
            return "(could not read log)";
        }
    }

    /// <summary>
    /// The last <paramref name="maxLines"/> lines of <paramref name="text"/> (further capped to
    /// <paramref name="maxChars"/>), for inlining a compact log preview in the issue body.
    /// </summary>
    public static string TailLines(string text, int maxLines = 15, int maxChars = 2000)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var tail = (lines.Length <= maxLines ? text.Replace("\r\n", "\n") : string.Join("\n", lines[^maxLines..])).TrimEnd();

        return tail.Length > maxChars ? "…\n" + tail[^maxChars..] : tail;
    }

    /// <summary>
    /// Replaces the current Windows username with a placeholder so it doesn't leak into a public
    /// issue (it appears in logged file paths such as <c>C:\Users\&lt;name&gt;\…</c>).
    /// </summary>
    public static string ScrubUser(string text)
    {
        var user = Environment.UserName;
        return string.IsNullOrEmpty(user)
            ? text
            : Regex.Replace(text, Regex.Escape(user), "%USER%", RegexOptions.IgnoreCase);
    }

    private static string Build(string labels, string title, string body) =>
        NewIssueUrl
        + "?labels=" + Uri.EscapeDataString(labels)
        + "&title=" + Uri.EscapeDataString(title)
        + "&body=" + Uri.EscapeDataString(body);

    private static string BugBody(string diagnostics, string logTail) => string.Join("\n",
        "### Describe the bug",
        "<!-- A clear, concise description of what went wrong. -->",
        "",
        "### Steps to reproduce",
        "1. ",
        "2. ",
        "3. ",
        "",
        "### Expected behavior",
        "<!-- What you expected to happen instead. -->",
        "",
        "### Recent log",
        "<!-- Auto-filled with the latest entries. Your full log is on the clipboard — paste it",
        "     below (or drag displayselector.log in) if more detail would help. -->",
        "```",
        logTail.Replace("\r\n", "\n").TrimEnd(),
        "```",
        "",
        "### Diagnostics",
        "```",
        diagnostics.Replace("\r\n", "\n").TrimEnd(), // uniform LF so the whole body is consistent
        "```",
        "",
        "### Additional context",
        "<!-- Anything else that might help. -->");

    private static string FeatureBody() => string.Join("\n",
        "### What problem would this solve?",
        "<!-- The use case or pain point. -->",
        "",
        "### Proposed solution",
        "<!-- What you'd like to happen. -->",
        "",
        "### Alternatives considered",
        "<!-- Other approaches you thought about. -->",
        "",
        "### Additional context",
        "<!-- Anything else (related setups, examples). -->");
}
