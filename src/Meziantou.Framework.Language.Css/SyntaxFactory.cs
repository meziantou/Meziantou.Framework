using Meziantou.Framework.Language.Css.Internals;
using Green = Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>Builds CSS nodes, tokens, and trivia.</summary>
/// <remarks>
/// A node built here is not part of any style sheet, so its span starts at zero. Putting it into a tree with
/// <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/> gives it a real position,
/// without re-reading any text. Nothing here adds whitespace that the CSS grammar does not require, so two tokens that
/// must not touch -- two type selectors of a descendant selector -- need trivia between them.
/// </remarks>
/// <example>
/// <code>
/// var declaration = SyntaxFactory.Declaration("color", SyntaxFactory.CssTokenValue(SyntaxFactory.Identifier("red")));
/// </code>
/// </example>
public static partial class SyntaxFactory
{
    /// <summary>A single space.</summary>
    public static SyntaxTrivia Space => Whitespace(" ");

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Whitespace(string text = " ") => Trivia(SyntaxKind.WhitespaceTrivia, text);

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia EndOfLine(string text = "\n") => Trivia(SyntaxKind.EndOfLineTrivia, text);

    /// <summary>Creates a <c>/* ... */</c> comment, adding the delimiters when <paramref name="text"/> does not have them.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> contains <c>*/</c> other than at its end.</exception>
    public static SyntaxTrivia Comment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var content = text.StartsWith("/*", StringComparison.Ordinal) && text.EndsWith("*/", StringComparison.Ordinal) && text.Length >= 4 ? text : "/* " + text + " */";
        if (content.IndexOf("*/", 2, StringComparison.Ordinal) != content.Length - 2)
            throw new ArgumentException("A comment cannot contain '*/', which would end it.", nameof(text));

        return Trivia(SyntaxKind.MultiLineCommentTrivia, content);
    }

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Trivia(SyntaxKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxTrivia(token: default, Green.SyntaxFactory.Trivia(kind, text), position: 0, index: 0);
    }

    /// <summary>Creates a token of <paramref name="kind"/>, spelled the only way that kind can be, such as <c>{</c>.</summary>
    public static SyntaxToken Token(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.Token(kind), position: 0, index: 0);

    /// <summary>Creates a token of <paramref name="kind"/> spelled <paramref name="text"/>, whose value is <paramref name="valueText"/>.</summary>
    /// <remarks>The text is used verbatim. Prefer <see cref="Identifier"/>, <see cref="StringToken"/>, and the like, which escape what needs to be.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Token(SyntaxKind kind, string text, string? valueText = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var value = valueText ?? text;
        var green = string.Equals(text, value, StringComparison.Ordinal)
            ? Green.SyntaxFactory.Token(leading: null, kind, text, trailing: null)
            : Green.SyntaxFactory.TokenWithValue(leading: null, kind, text, value, value, trailing: null);

        return new SyntaxToken(parent: null, green, position: 0, index: 0);
    }

    /// <summary>Creates a zero-width token standing in for one the source does not have.</summary>
    public static SyntaxToken MissingToken(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.MissingToken(kind), position: 0, index: 0);

    /// <summary>Creates an identifier whose value is <paramref name="value"/>, escaping the characters an identifier cannot hold as they are.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/> or empty.</exception>
    public static SyntaxToken Identifier(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        return Token(SyntaxKind.IdentToken, CssIdentifier.SerializeIdentifier(value), value);
    }

    /// <summary>Creates a string whose value is <paramref name="value"/>, between <paramref name="quote"/> characters.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quote"/> is neither a double nor a single quote.</exception>
    public static SyntaxToken StringToken(string value, char quote = '"')
    {
        ArgumentNullException.ThrowIfNull(value);
        if (quote is not ('"' or '\''))
            throw new ArgumentOutOfRangeException(nameof(quote), quote, "A CSS string is between double or single quotes.");

        return Token(SyntaxKind.StringToken, CssIdentifier.SerializeString(value, quote), value);
    }

    /// <summary>Creates a number token.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not finite, which CSS cannot write.</exception>
    public static SyntaxToken Number(double value) => Numeric(SyntaxKind.NumberToken, value, FormatNumber(value));

    /// <summary>Creates a percentage token, such as <c>50%</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not finite, which CSS cannot write.</exception>
    public static SyntaxToken Percentage(double value) => Numeric(SyntaxKind.PercentageToken, value, FormatNumber(value) + "%");

    /// <summary>Creates a dimension token, such as <c>10px</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not finite, which CSS cannot write.</exception>
    /// <exception cref="ArgumentException"><paramref name="unit"/> is <see langword="null"/> or empty.</exception>
    public static SyntaxToken Dimension(double value, string unit)
    {
        ArgumentException.ThrowIfNullOrEmpty(unit);

        var serializedUnit = CssIdentifier.SerializeIdentifier(unit);

        // A unit that reads as an exponent, such as "e3", would turn "1e3" into the number 1000.
        if (serializedUnit[0] is 'e' or 'E' && serializedUnit.Length > 1 && (char.IsAsciiDigit(serializedUnit[1]) || (serializedUnit[1] is '+' or '-' && serializedUnit.Length > 2 && char.IsAsciiDigit(serializedUnit[2]))))
        {
            serializedUnit = "\\" + ((int)serializedUnit[0]).ToString("x", CultureInfo.InvariantCulture) + " " + serializedUnit[1..];
        }

        return Numeric(SyntaxKind.DimensionToken, value, FormatNumber(value) + serializedUnit);
    }

    /// <summary>Creates a hash token, such as <c>#main</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public static SyntaxToken Hash(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Token(SyntaxKind.HashToken, "#" + SerializeName(name), name);
    }

    /// <summary>Creates an at-keyword token, such as <c>@media</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public static SyntaxToken AtKeyword(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Token(SyntaxKind.AtKeywordToken, "@" + CssIdentifier.SerializeIdentifier(name), name);
    }

    /// <summary>Creates a function token, such as <c>rgb(</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public static SyntaxToken FunctionToken(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return Token(SyntaxKind.FunctionToken, CssIdentifier.SerializeIdentifier(name) + "(", name);
    }

    /// <summary>Creates a declaration such as <c>color: red;</c>, with a space before each value and a semicolon after the last one.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public static CssDeclarationSyntax Declaration(string name, params CssComponentValueSyntax[] values)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(values);

        var spaced = values.Select(value => value.GetLeadingTrivia().Count > 0 ? value : value.WithLeadingTrivia(Space));
        return CssDeclaration(Identifier(name), Token(SyntaxKind.ColonToken), new SyntaxList<CssComponentValueSyntax>(spaced), important: null, Token(SyntaxKind.SemicolonToken));
    }

    /// <summary>Parses <paramref name="text"/> into a tree.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static CssSyntaxTree ParseSyntaxTree(string text, CssParseOptions? options = null, string? path = null) => CssSyntaxTree.ParseText(text, options, path);

    /// <summary>Parses <paramref name="text"/> as a style sheet, and returns its root.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static CssStyleSheetSyntax ParseStyleSheet(string text, CssParseOptions? options = null) => CssSyntaxTree.ParseText(text, options).GetRoot();

    private static SyntaxToken Numeric(SyntaxKind kind, double value, string text)
        => new(parent: null, Green.SyntaxFactory.TokenWithValue(leading: null, kind, text, value, text, trailing: null), position: 0, index: 0);

    private static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "CSS cannot write a number that is not finite.");

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Writes the name of a hash token, which, unlike an identifier, may start with a digit or a dash.</summary>
    private static string SerializeName(string name)
    {
        var serialized = CssIdentifier.SerializeIdentifier("a" + name);
        return serialized[1..];
    }

    /// <summary>Unwraps a token that a node requires, rejecting the default one no factory should produce.</summary>
    private static Meziantou.Framework.Language.InternalSyntax.GreenNode Required(SyntaxToken token)
        => token.Node ?? throw new ArgumentException("A required token was not given.", nameof(token));
}
