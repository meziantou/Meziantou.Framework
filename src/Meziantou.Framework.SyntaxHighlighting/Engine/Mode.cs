namespace Meziantou.Framework.SyntaxHighlighting.Engine;

internal sealed class Mode
{
    public string? Scope { get; init; }
    public string? Match { get; init; }
    public string? Begin { get; init; }
    public string? End { get; init; }
    public IReadOnlyList<string>? BeginParts { get; init; }
    public IReadOnlyDictionary<int, string>? BeginScope { get; init; }
    // Single scope to wrap the end lexeme in (highlight.js's `endScope: "name"` /
    // `_wrap`). Cannot be combined with ExcludeEnd / ReturnEnd.
    public string? EndScope { get; init; }
    // Named guard executed against each begin match candidate. If the guard
    // rejects, the engine advances and looks for the next match within the same
    // child mode. Mirrors highlight.js's `on:begin` callback when used as a veto
    // (e.g. JSX tag false-positive detection). See Engine/BeginGuards.cs.
    public string? BeginGuard { get; init; }
    public string? SubLanguage { get; init; }
    public IList<string>? BeginKeywords { get; init; }
    public string? Illegal { get; init; }
    public Keywords? Keywords { get; init; }
    public string? KeywordPattern { get; init; }
    public KeywordValidator? KeywordValidator { get; init; }
    public IList<Mode> Contains { get; set; } = Array.Empty<Mode>();
    public IReadOnlyList<Mode>? Variants { get; set; }
    public Mode? Starts { get; set; }
    public bool ExcludeBegin { get; init; }
    public bool ExcludeEnd { get; init; }
    public bool ReturnBegin { get; init; }
    public bool ReturnEnd { get; init; }
    public bool EndsWithParent { get; init; }
    public bool EndsParent { get; init; }
    public bool EndSameAsBegin { get; init; }
    public bool CaseInsensitive { get; init; }
    // hljs `skip: true`: match begin/end and accumulate the matched region into
    // the parent's buffer as plain text. No scope is opened, no inner buffer
    // flush happens, so the parent's sub-language (e.g. csharp inside a Razor
    // `@{ ... }` block) can re-tokenize the whole region as a unit.
    public bool Skip { get; init; }
    // When set on a variant, suppresses the parent mode's Scope during expansion.
    // Mirrors highlight.js's `className: null` override.
    public bool ClearScope { get; init; }
    public IReadOnlyDictionary<string, string>? ClassNameAliases { get; init; }

    public static readonly Mode Self = new();

    internal Mode With(Action<Builder> configure)
    {
        var b = new Builder(this);
        configure(b);
        return b.Build();
    }

    internal sealed class Builder
    {
        public string? Scope;
        public string? Match;
        public string? Begin;
        public string? End;
        public string? EndScope;
        public string? BeginGuard;
        public string? SubLanguage;
        public IReadOnlyList<string>? BeginParts;
        public IReadOnlyDictionary<int, string>? BeginScope;
        public IList<string>? BeginKeywords;
        public string? Illegal;
        public Keywords? Keywords;
        public string? KeywordPattern;
        public KeywordValidator? KeywordValidator;
        public IList<Mode> Contains;
        public IReadOnlyList<Mode>? Variants;
        public Mode? Starts;
        public bool ExcludeBegin;
        public bool ExcludeEnd;
        public bool ReturnBegin;
        public bool ReturnEnd;
        public bool EndsWithParent;
        public bool EndsParent;
        public bool EndSameAsBegin;
        public bool CaseInsensitive;
        public bool Skip;
        public bool ClearScope;
        public IReadOnlyDictionary<string, string>? ClassNameAliases;

        public Builder(Mode src)
        {
            Scope = src.Scope;
            Match = src.Match;
            Begin = src.Begin;
            End = src.End;
            EndScope = src.EndScope;
            BeginGuard = src.BeginGuard;
            SubLanguage = src.SubLanguage;
            BeginParts = src.BeginParts;
            BeginScope = src.BeginScope;
            BeginKeywords = src.BeginKeywords;
            Illegal = src.Illegal;
            Keywords = src.Keywords;
            KeywordPattern = src.KeywordPattern;
            KeywordValidator = src.KeywordValidator;
            Contains = src.Contains;
            Variants = src.Variants;
            Starts = src.Starts;
            ExcludeBegin = src.ExcludeBegin;
            ExcludeEnd = src.ExcludeEnd;
            ReturnBegin = src.ReturnBegin;
            ReturnEnd = src.ReturnEnd;
            EndsWithParent = src.EndsWithParent;
            EndsParent = src.EndsParent;
            EndSameAsBegin = src.EndSameAsBegin;
            CaseInsensitive = src.CaseInsensitive;
            Skip = src.Skip;
            ClearScope = src.ClearScope;
            ClassNameAliases = src.ClassNameAliases;
        }

        public Mode Build() => new()
        {
            Scope = Scope,
            Match = Match,
            Begin = Begin,
            End = End,
            EndScope = EndScope,
            BeginGuard = BeginGuard,
            SubLanguage = SubLanguage,
            BeginParts = BeginParts,
            BeginScope = BeginScope,
            BeginKeywords = BeginKeywords,
            Illegal = Illegal,
            Keywords = Keywords,
            KeywordPattern = KeywordPattern,
            KeywordValidator = KeywordValidator,
            Contains = Contains,
            Variants = Variants,
            Starts = Starts,
            ExcludeBegin = ExcludeBegin,
            ExcludeEnd = ExcludeEnd,
            ReturnBegin = ReturnBegin,
            ReturnEnd = ReturnEnd,
            EndsWithParent = EndsWithParent,
            EndsParent = EndsParent,
            EndSameAsBegin = EndSameAsBegin,
            CaseInsensitive = CaseInsensitive,
            Skip = Skip,
            ClearScope = ClearScope,
            ClassNameAliases = ClassNameAliases,
        };
    }
}
