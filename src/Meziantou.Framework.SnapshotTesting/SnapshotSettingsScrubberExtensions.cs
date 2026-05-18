using System.Text.RegularExpressions;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Provides extension methods for adding scrubbers to <see cref="SnapshotSettings"/>.
/// </summary>
public static class SnapshotSettingsScrubberExtensions
{
    extension(SnapshotSettings settings)
    {
        /// <summary>Adds a scrubber that removes lines matching the specified predicate.</summary>
        public void ScrubLines(Func<string, bool> predicate) => settings.Scrubbers.Add(new LineFilterScrubber(predicate));

        /// <summary>Adds a scrubber that removes lines containing any of the specified text values (case-insensitive).</summary>
        public void ScrubLinesContaining(params string[] searchText) => settings.ScrubLinesContaining(StringComparison.OrdinalIgnoreCase, searchText);

        /// <summary>Adds a scrubber that removes lines containing any of the specified text values with the specified comparison.</summary>
        public void ScrubLinesContaining(StringComparison stringComparison, params string[] searchText)
        {
            foreach (var text in searchText)
            {
                settings.Scrubbers.Add(new LineFilterScrubber(line => line.Contains(text, stringComparison)));
            }
        }

        /// <summary>Adds a scrubber that removes lines matching the specified regular expression.</summary>
        public void ScrubLinesMatching(Regex regex) => settings.Scrubbers.Add(new LineFilterScrubber(regex.IsMatch));

        /// <summary>Adds a scrubber that removes lines matching the specified regular expression pattern.</summary>
        public void ScrubLinesMatching([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
            => settings.ScrubLinesMatching(pattern, RegexOptions.None, Timeout.InfiniteTimeSpan);

        /// <summary>Adds a scrubber that removes lines matching the specified regular expression pattern with options.</summary>
        public void ScrubLinesMatching([StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexOptions options)
            => settings.ScrubLinesMatching(pattern, options, Timeout.InfiniteTimeSpan);

        /// <summary>Adds a scrubber that removes lines matching the specified regular expression pattern with options and timeout.</summary>
        public void ScrubLinesMatching([StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexOptions options, TimeSpan matchTimeout)
            => settings.Scrubbers.Add(new LineFilterScrubber(line => Regex.IsMatch(line, pattern, options, matchTimeout)));

        /// <summary>Adds a scrubber that replaces each line using the specified function.</summary>
        public void ScrubLinesWithReplace(Func<string, string?> replaceLine) => settings.Scrubbers.Add(new LineReplaceScrubber(replaceLine));

        /// <summary>Adds a scrubber that replaces the machine name with a consistent value.</summary>
        public void ScrubMachineName() => settings.Scrubbers.Add(new LineReplaceScrubber(line => line.Replace(Environment.MachineName, "TheMachineName", StringComparison.OrdinalIgnoreCase)));

        /// <summary>Adds a scrubber that replaces the user name with a consistent value.</summary>
        public void ScrubUserName() => settings.Scrubbers.Add(new LineReplaceScrubber(line => line.Replace(Environment.UserName, "TheUserName", StringComparison.OrdinalIgnoreCase)));
    }
}
