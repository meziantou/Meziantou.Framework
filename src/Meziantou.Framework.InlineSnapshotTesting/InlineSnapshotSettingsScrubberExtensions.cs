using System.Text.RegularExpressions;

namespace Meziantou.Framework.InlineSnapshotTesting;

public static class InlineSnapshotSettingsScrubberExtensions
{
    public static void ScrubLines(this InlineSnapshotSettings settings, Func<string, bool> predicate) => settings.Scrubbers.Add(new LineFilterScrubber(predicate));
    public static void ScrubLinesContaining(this InlineSnapshotSettings settings, params string[] searchText) => settings.ScrubLinesContaining(StringComparison.OrdinalIgnoreCase, searchText);
    public static void ScrubLinesContaining(this InlineSnapshotSettings settings, StringComparison stringComparison, params string[] searchText)
    {
        foreach (var text in searchText)
        {
            settings.Scrubbers.Add(new LineFilterScrubber(line => line.Contains(text, stringComparison)));
        }
    }

    public static void ScrubLinesMatching(this InlineSnapshotSettings settings, Regex regex) => settings.Scrubbers.Add(new LineFilterScrubber(regex.IsMatch));

    public static void ScrubLinesMatching(this InlineSnapshotSettings settings, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
        => ScrubLinesMatching(settings, pattern, RegexOptions.None, Timeout.InfiniteTimeSpan);

    public static void ScrubLinesMatching(this InlineSnapshotSettings settings, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexOptions options)
        => ScrubLinesMatching(settings, pattern, options, Timeout.InfiniteTimeSpan);

    public static void ScrubLinesMatching(this InlineSnapshotSettings settings, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexOptions options, TimeSpan matchTimeout)
        => settings.Scrubbers.Add(new LineFilterScrubber(line => Regex.IsMatch(line, pattern, options, matchTimeout)));

    public static void ScrubLinesWithReplace(this InlineSnapshotSettings settings, Func<string, string?> replaceLine) => settings.Scrubbers.Add(new LineReplaceScrubber(replaceLine));

    public static void ScrubMachineName(this InlineSnapshotSettings settings) => settings.Scrubbers.Add(new LineReplaceScrubber(line => line.Replace(Environment.MachineName, "TheMachineName", StringComparison.OrdinalIgnoreCase)));
    public static void ScrubUserName(this InlineSnapshotSettings settings) => settings.Scrubbers.Add(new LineReplaceScrubber(line => line.Replace(Environment.UserName, "TheUserName", StringComparison.OrdinalIgnoreCase)));
}
