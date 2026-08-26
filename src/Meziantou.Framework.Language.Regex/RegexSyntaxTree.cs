using Meziantou.Framework.Language.Regex.Internals;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents an immutable regular-expression syntax tree with source text and diagnostics.</summary>
public sealed class RegexSyntaxTree
{
    private readonly List<RegexDiagnostic> _diagnostics;

    private RegexSyntaxTree(string text, RegexParseOptions options, RegexPatternSyntax root, List<RegexDiagnostic> diagnostics, IReadOnlyList<RegexCaptureInfo> captures, RegexPatternOptions patternOptions)
    {
        Text = text;
        SourceText = SourceText.From(text);
        Options = options;
        Root = root;
        Captures = captures;
        PatternOptions = patternOptions;
        _diagnostics = diagnostics;
        Root.SetParentAndTree(parent: null, this);
    }

    public string Text { get; }
    public SourceText SourceText { get; }
    public RegexParseOptions Options { get; }

    /// <summary>The flavor the pattern was parsed as.</summary>
    public RegexFlavor Flavor => Options.Flavor;

    /// <summary>
    /// The options in effect at the start of the pattern, including the ones read from a JavaScript literal's flags.
    /// </summary>
    public RegexPatternOptions PatternOptions { get; }

    public RegexPatternSyntax Root { get; }
    public IReadOnlyList<RegexDiagnostic> Diagnostics => _diagnostics;

    /// <summary>The capture groups the pattern declares, in the order the engine numbers them.</summary>
    public IReadOnlyList<RegexCaptureInfo> Captures { get; }

    public RegexPatternSyntax GetRoot() => Root;
    public IReadOnlyList<RegexDiagnostic> GetDiagnostics() => Diagnostics;

    /// <summary>Parses <paramref name="pattern"/> as a complete pattern. Never throws; problems are reported as diagnostics.</summary>
    public static RegexSyntaxTree ParseText([StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(flavor);

        return ParseText(pattern, new RegexParseOptions(flavor));
    }

    /// <inheritdoc cref="ParseText(string, RegexFlavor)"/>
    public static RegexSyntaxTree ParseText([StringSyntax(StringSyntaxAttribute.Regex)] string pattern, RegexParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Parse(pattern ?? string.Empty, options, literal: null);
    }

    /// <summary>
    /// Parses a JavaScript regular-expression literal such as <c>/a+/giu</c>, reading the flag letters into
    /// <see cref="PatternOptions"/> and keeping the delimiters on the root so the literal round-trips.
    /// </summary>
    /// <remarks>
    /// The flavor is always <see cref="RegexFlavor.JavaScript"/>: only that flavor has literals. Text that follows the
    /// flags is reported as <c>REGEX0204</c> and kept as skipped text.
    /// </remarks>
    public static RegexSyntaxTree ParseJavaScriptLiteral(string literal)
    {
        var text = literal ?? string.Empty;
        var parsed = JavaScriptLiteral.Split(text);

        return Parse(text, new RegexParseOptions(RegexFlavor.JavaScript) { PatternOptions = parsed.Options }, parsed);
    }

    private static RegexSyntaxTree Parse(string text, RegexParseOptions options, JavaScriptLiteral? literal)
    {
        // A pattern is numbered before it is parsed, because a backreference may name a group declared after it. The
        // numbering walk is the same parser over the same text, so the two cannot disagree about where the groups are.
        var captureTable = CreateParser(text, options, literal).CollectCaptureTable();

        var parser = CreateParser(text, options, literal);
        var root = parser.ParsePattern(captureTable);

        return new RegexSyntaxTree(text, options, root, [.. parser.Diagnostics], parser.Captures, options.PatternOptions);
    }

    /// <summary>The flavor family selects the parser; flavor features handle the differences within a family.</summary>
    private static RegexParser CreateParser(string text, RegexParseOptions options, JavaScriptLiteral? literal) => options.Flavor.Family switch
    {
        RegexFlavorFamily.JavaScript => new JavaScriptRegexParser(text, options, literal),
        RegexFlavorFamily.Pcre => new PcreRegexParser(text, options),
        RegexFlavorFamily.Posix => new PosixRegexParser(text, options),
        _ => new NetRegexParser(text, options),
    };

    public RegexSyntaxTree WithChanges(params RegexTextChange[] changes) => WithChanges((IEnumerable<RegexTextChange>)changes);

    public RegexSyntaxTree WithChanges(IEnumerable<RegexTextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return Reparse(SourceText.WithChanges(changes).Text);
    }

    /// <summary>
    /// Reparses <paramref name="text"/> the way this tree was parsed, literal delimiters included.
    /// </summary>
    /// <remarks>
    /// A tree built by <see cref="ParseJavaScriptLiteral"/> has to go back through it. Reparsing <c>/a/g</c> as a bare
    /// pattern would read the delimiters as literal slashes and the flags as ordinary characters, so an edit anywhere
    /// in the pattern would quietly destroy the structure around it.
    /// </remarks>
    internal RegexSyntaxTree Reparse(string text) =>
        Root.IsJavaScriptLiteral ? ParseJavaScriptLiteral(text) : ParseText(text, Options);

    /// <summary>
    /// Returns the edit that turns <paramref name="oldTree"/>'s text into this tree's text. The common prefix and
    /// suffix are trimmed, so an edit in the middle of a pattern reports only the part that actually differs.
    /// </summary>
    public IReadOnlyList<RegexTextChange> GetChanges(RegexSyntaxTree oldTree)
    {
        ArgumentNullException.ThrowIfNull(oldTree);

        var oldText = oldTree.Text;
        var newText = Text;
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
            return [];

        var prefix = 0;
        var maxPrefix = Math.Min(oldText.Length, newText.Length);
        while (prefix < maxPrefix && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }

        // Never split a surrogate pair: the two halves are not text on their own.
        if (prefix > 0 && char.IsHighSurrogate(oldText[prefix - 1]))
        {
            prefix--;
        }

        var suffix = 0;
        var maxSuffix = Math.Min(oldText.Length, newText.Length) - prefix;
        while (suffix < maxSuffix && oldText[oldText.Length - suffix - 1] == newText[newText.Length - suffix - 1])
        {
            suffix++;
        }

        if (suffix > 0 && char.IsLowSurrogate(oldText[oldText.Length - suffix]))
        {
            suffix--;
        }

        return [new RegexTextChange(
            TextSpan.FromBounds(prefix, oldText.Length - suffix),
            newText[prefix..(newText.Length - suffix)])];
    }

    /// <summary>
    /// Compares this tree with <paramref name="other"/> structurally, ignoring extended-mode whitespace and comments.
    /// Two patterns parsed as different flavors, or with different options, are never equivalent.
    /// </summary>
    /// <remarks>
    /// Identical text is a shortcut only when the options match as well. The same characters read with and without
    /// <see cref="RegexPatternOptions.IgnorePatternWhitespace"/> are different trees -- a space is a term in one and
    /// trivia in the other -- so comparing the text alone would call them equivalent.
    /// </remarks>
    public bool IsEquivalentTo(RegexSyntaxTree? other)
    {
        if (other is null)
            return false;

        if (other.Flavor != Flavor)
            return false;

        if (other.PatternOptions == PatternOptions && string.Equals(Text, other.Text, StringComparison.Ordinal))
            return true;

        return Root.IsEquivalentTo(other.Root);
    }
}
