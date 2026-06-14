using System.Linq;
using DisplaySelector.Core;
using Xunit;

namespace DisplaySelector.Tests;

public class IssueReporterTests
{
    [Fact]
    public void BugReportUrl_targets_the_new_issue_endpoint_with_bug_label()
    {
        var url = IssueReporter.BugReportUrl("OS : Windows 11", "log line");

        Assert.StartsWith($"{AppIdentity.ProjectUrl}/issues/new?", url);
        Assert.Contains("labels=bug", url);
        Assert.Contains("title=%5BBUG%5D%20", url); // "[BUG] " percent-encoded
    }

    [Fact]
    public void BugReportUrl_inlines_the_diagnostics_text()
    {
        var url = IssueReporter.BugReportUrl("MARKER_12345", "");

        Assert.Contains("MARKER_12345", Uri.UnescapeDataString(url));
    }

    [Fact]
    public void BugReportUrl_inlines_the_log_tail()
    {
        var url = IssueReporter.BugReportUrl("diag", "LOGMARK_999");

        Assert.Contains("LOGMARK_999", Uri.UnescapeDataString(url));
    }

    [Fact]
    public void BugReportUrl_normalizes_crlf_in_the_body()
    {
        var url = IssueReporter.BugReportUrl("line one\r\nline two\r\n", "log\r\nline");

        Assert.DoesNotContain("\r", Uri.UnescapeDataString(url));
    }

    [Fact]
    public void BugReportUrl_truncates_oversized_diagnostics_to_stay_under_the_url_cap()
    {
        var huge = new string('x', 50_000);

        var url = IssueReporter.BugReportUrl(huge, "log line");

        Assert.True(url.Length <= 7000, $"URL length was {url.Length}");
        Assert.Contains("truncated", Uri.UnescapeDataString(url));
    }

    [Fact]
    public void TailLines_keeps_only_the_last_lines()
    {
        var text = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"line {i}"));

        var tail = IssueReporter.TailLines(text, maxLines: 5);

        Assert.DoesNotContain("line 45", tail);
        Assert.Contains("line 46", tail);
        Assert.Contains("line 50", tail);
    }

    [Fact]
    public void TailLines_caps_total_characters()
    {
        var text = new string('y', 10_000);

        var tail = IssueReporter.TailLines(text, maxLines: 15, maxChars: 2000);

        Assert.True(tail.Length <= 2002, $"tail length was {tail.Length}"); // 2000 + leading "…\n"
    }

    [Fact]
    public void FeatureRequestUrl_uses_the_enhancement_label()
    {
        var url = IssueReporter.FeatureRequestUrl();

        Assert.StartsWith($"{AppIdentity.ProjectUrl}/issues/new?", url);
        Assert.Contains("labels=enhancement", url);
        Assert.Contains("title=%5BFEATURE%5D%20", url);
    }

    [Fact]
    public void ScrubUser_replaces_the_current_username()
    {
        var user = Environment.UserName;
        Assert.False(string.IsNullOrEmpty(user)); // sanity for the test environment

        var scrubbed = IssueReporter.ScrubUser($@"C:\Users\{user}\AppData\Local\file.log");

        Assert.DoesNotContain(user, scrubbed);
        Assert.Contains("%USER%", scrubbed);
    }

    [Fact]
    public void ScrubUser_is_case_insensitive()
    {
        var user = Environment.UserName;

        var scrubbed = IssueReporter.ScrubUser(user.ToUpperInvariant());

        Assert.Equal("%USER%", scrubbed);
    }
}
