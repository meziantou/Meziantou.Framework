using System.Text.RegularExpressions;

namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class RegexScrubber : Scrubber
{
    private readonly Regex _regex;
    private readonly string _replacement;

    public RegexScrubber(Regex regex, string replacement)
    {
        _regex = regex;
        _replacement = replacement;
    }

    public override string Scrub(string text)
    {
        return _regex.Replace(text, _replacement);
    }
}
