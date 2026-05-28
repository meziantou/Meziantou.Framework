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
    public Regex? KeywordPatternRe;
    public Dictionary<string, KeywordHit>? KeywordMap;
    public KeywordValidator? KeywordValidator;
    public List<CompiledMode> Contains = [];
    public CompiledMode? Starts;
    public CompiledMode? Parent;
    public IReadOnlyDictionary<string, string>? ClassNameAliases;
    public IReadOnlyDictionary<int, string>? BeginGroupScopes;
    public IReadOnlyList<int>? BeginGroupOrder;
    public string? SubLanguage;
    public bool EndSameAsBegin;
    public bool Skip;
    public string? BeginGuard;
    public string? EndScope;
}
