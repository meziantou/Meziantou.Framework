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

    public Mode()
    {
    }

    /// <summary>
    /// Copies every member of <paramref name="source"/>, so that an object initializer can then
    /// override the members that differ (used to expand <see cref="Variants"/>).
    /// </summary>
    public Mode(Mode source)
    {
        Scope = source.Scope;
        Match = source.Match;
        Begin = source.Begin;
        End = source.End;
        BeginParts = source.BeginParts;
        BeginScope = source.BeginScope;
        EndScope = source.EndScope;
        BeginGuard = source.BeginGuard;
        SubLanguage = source.SubLanguage;
        BeginKeywords = source.BeginKeywords;
        Illegal = source.Illegal;
        Keywords = source.Keywords;
        KeywordPattern = source.KeywordPattern;
        KeywordValidator = source.KeywordValidator;
        Contains = source.Contains;
        Variants = source.Variants;
        Starts = source.Starts;
        ExcludeBegin = source.ExcludeBegin;
        ExcludeEnd = source.ExcludeEnd;
        ReturnBegin = source.ReturnBegin;
        ReturnEnd = source.ReturnEnd;
        EndsWithParent = source.EndsWithParent;
        EndsParent = source.EndsParent;
        EndSameAsBegin = source.EndSameAsBegin;
        CaseInsensitive = source.CaseInsensitive;
        Skip = source.Skip;
        ClearScope = source.ClearScope;
        ClassNameAliases = source.ClassNameAliases;
    }
}
