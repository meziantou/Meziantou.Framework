using System.Text.RegularExpressions;

namespace Meziantou.Framework.SyntaxHighlighting.Engine;

internal sealed class CompiledMode
{
    public Mode Source = null!;
    public string? Scope;
    public bool ExcludeBegin;
    public bool ExcludeEnd;
    public bool ReturnBegin;
    public bool ReturnEnd;
    public bool EndsWithParent;
    public bool EndsParent;
    public Regex? BeginRe;
    public Regex? EndRe;
    public Regex? IllegalRe;

    // Index of each regex in the per-run scan cache (see Tokenizer). Unique within one grammar.
    public int BeginSlot = -1;
    public int EndSlot = -1;
    public int IllegalSlot = -1;

    // Only set on a grammar's root: the number of slots the grammar's regexes use, and their match timeout.
    public int RegexSlotCount;
    public TimeSpan MatchTimeout;
    public Regex? KeywordPatternRe;

    // Keyword to scope; a null scope means "a keyword, emitted as plain text".
    public Dictionary<string, string?>? KeywordMap;
    public KeywordValidator? KeywordValidator;
    public List<CompiledMode> Contains = [];
    public CompiledMode? Starts;
    public IReadOnlyDictionary<string, string>? ClassNameAliases;
    public IReadOnlyDictionary<int, string>? BeginGroupScopes;
    public IReadOnlyList<int>? BeginGroupOrder;
    public string? SubLanguage;
    public bool EndSameAsBegin;
    public bool Skip;
    public string? BeginGuard;
    public string? EndScope;
}
