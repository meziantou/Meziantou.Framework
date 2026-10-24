using System.Text.RegularExpressions;

namespace Meziantou.Framework.Pdf.Corpus;

/// <summary>
/// The files that are not run (<c>corpus-skip.txt</c>): an identifier, a directory (ending with <c>/</c>), or a pattern
/// (<c>*</c> matches within a segment, <c>**</c> across segments), each preceded by a comment that gives the reason.
/// </summary>
internal sealed class CorpusSkipList
{
    private readonly List<Regex> _patterns = [];

    private CorpusSkipList(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var pattern = "^" + Regex.Escape(line).Replace(@"\*\*", ".*", StringComparison.Ordinal).Replace(@"\*", "[^/]*", StringComparison.Ordinal);
            pattern += line.EndsWith('/', StringComparison.Ordinal) ? ".*$" : "$";
            _patterns.Add(new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)));
        }
    }

    public static CorpusSkipList Default { get; } = new(File.Exists(CorpusFiles.SkipFile) ? File.ReadAllLines(CorpusFiles.SkipFile) : []);

    public bool IsSkipped(string id) => _patterns.Exists(p => p.IsMatch(id));
}
