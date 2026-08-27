using System.Text.RegularExpressions;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Provides extension methods for adding scrubbers to <see cref="SnapshotSettings"/>.
/// </summary>
public static class SnapshotSettingsScrubberExtensions
{
    /// <summary>
    /// The match timeout applied when a caller does not specify one. The pattern comes from the caller, so an
    /// unbounded match would let a pattern that backtracks catastrophically hang the test run outright.
    /// </summary>
    public static TimeSpan DefaultMatchTimeout { get; } = TimeSpan.FromSeconds(1);

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
        /// <remarks>Matching is bounded by <see cref="DefaultMatchTimeout" />.</remarks>
        public void ScrubLinesMatching([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
            => settings.ScrubLinesMatching(pattern, RegexOptions.None, DefaultMatchTimeout);

        /// <summary>Adds a scrubber that removes lines matching the specified regular expression pattern with options.</summary>
        /// <remarks>Matching is bounded by <see cref="DefaultMatchTimeout" />.</remarks>
        public void ScrubLinesMatching([StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexOptions options)
            => settings.ScrubLinesMatching(pattern, options, DefaultMatchTimeout);

        /// <summary>Adds a scrubber that removes lines matching the specified regular expression pattern with options and timeout.</summary>
        public void ScrubLinesMatching([StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexOptions options, TimeSpan matchTimeout)
        {
            // Building the Regex here rather than calling the static Regex.IsMatch per line keeps the pattern
            // out of the process-wide regex cache, which holds 15 entries and thrashes past that, and reports
            // an invalid pattern when the scrubber is registered instead of on the first line scrubbed.
            var regex = new Regex(pattern, options, matchTimeout);
            settings.Scrubbers.Add(new LineFilterScrubber(regex.IsMatch));
        }

        /// <summary>Adds a scrubber that replaces each line using the specified function.</summary>
        public void ScrubLinesWithReplace(Func<string, string?> replaceLine) => settings.Scrubbers.Add(new LineReplaceScrubber(replaceLine));

        /// <summary>Adds a scrubber that replaces the machine name with a consistent value.</summary>
        public void ScrubMachineName() => settings.Scrubbers.Add(new LineReplaceScrubber(line => line.Replace(Environment.MachineName, "TheMachineName", StringComparison.OrdinalIgnoreCase)));

        /// <summary>Adds a scrubber that replaces the user name with a consistent value.</summary>
        public void ScrubUserName() => settings.Scrubbers.Add(new LineReplaceScrubber(line => line.Replace(Environment.UserName, "TheUserName", StringComparison.OrdinalIgnoreCase)));
    }
}
