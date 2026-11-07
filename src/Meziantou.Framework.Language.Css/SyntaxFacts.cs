using Meziantou.Framework.Language.Css.Internals;

namespace Meziantou.Framework.Language.Css;

/// <summary>Answers questions about CSS kinds and names without needing a tree.</summary>
public static class SyntaxFacts
{
    /// <summary>Gets the text of a token whose kind fixes it, or an empty string for any other kind.</summary>
    public static string GetText(SyntaxKind kind) => kind switch
    {
        SyntaxKind.ColonToken => ":",
        SyntaxKind.SemicolonToken => ";",
        SyntaxKind.CommaToken => ",",
        SyntaxKind.OpenBracketToken => "[",
        SyntaxKind.CloseBracketToken => "]",
        SyntaxKind.OpenParenToken => "(",
        SyntaxKind.CloseParenToken => ")",
        SyntaxKind.OpenBraceToken => "{",
        SyntaxKind.CloseBraceToken => "}",
        SyntaxKind.CdoToken => "<!--",
        SyntaxKind.CdcToken => "-->",
        SyntaxKind.DotToken => ".",
        SyntaxKind.AsteriskToken => "*",
        SyntaxKind.AmpersandToken => "&",
        SyntaxKind.GreaterThanToken => ">",
        SyntaxKind.LessThanToken => "<",
        SyntaxKind.EqualsToken => "=",
        SyntaxKind.PlusToken => "+",
        SyntaxKind.MinusToken => "-",
        SyntaxKind.TildeToken => "~",
        SyntaxKind.BarToken => "|",
        SyntaxKind.CaretToken => "^",
        SyntaxKind.DollarToken => "$",
        SyntaxKind.ExclamationToken => "!",
        SyntaxKind.SlashToken => "/",
        _ => "",
    };

    /// <summary>Gets the kind of the delimiter <paramref name="c"/>, or <see cref="SyntaxKind.DelimToken"/> when it has none of its own.</summary>
    public static SyntaxKind GetDelimKind(char c) => c switch
    {
        '.' => SyntaxKind.DotToken,
        '*' => SyntaxKind.AsteriskToken,
        '&' => SyntaxKind.AmpersandToken,
        '>' => SyntaxKind.GreaterThanToken,
        '<' => SyntaxKind.LessThanToken,
        '=' => SyntaxKind.EqualsToken,
        '+' => SyntaxKind.PlusToken,
        '-' => SyntaxKind.MinusToken,
        '~' => SyntaxKind.TildeToken,
        '|' => SyntaxKind.BarToken,
        '^' => SyntaxKind.CaretToken,
        '$' => SyntaxKind.DollarToken,
        '!' => SyntaxKind.ExclamationToken,
        '/' => SyntaxKind.SlashToken,
        _ => SyntaxKind.DelimToken,
    };

    /// <summary>Determines whether <paramref name="kind"/> is a delim token of the tokenizer, with a kind of its own or not.</summary>
    public static bool IsDelim(SyntaxKind kind) => kind is >= SyntaxKind.DelimToken and <= SyntaxKind.SlashToken;

    public static bool IsTrivia(SyntaxKind kind) => kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia or SyntaxKind.MultiLineCommentTrivia;

    public static bool IsComment(SyntaxKind kind) => kind is SyntaxKind.MultiLineCommentTrivia;

    public static bool IsAnyToken(SyntaxKind kind) => kind is >= SyntaxKind.IdentToken and <= SyntaxKind.EndOfFileToken;

    /// <summary>Determines whether <paramref name="kind"/> is a token whose value is a number: a number, a percentage, or a dimension.</summary>
    public static bool IsNumeric(SyntaxKind kind) => kind is SyntaxKind.NumberToken or SyntaxKind.PercentageToken or SyntaxKind.DimensionToken;

    /// <summary>Determines whether <paramref name="kind"/> is a rule, qualified or at-rule.</summary>
    public static bool IsRule(SyntaxKind kind) => kind is >= SyntaxKind.StyleRule and <= SyntaxKind.UnknownAtRule;

    /// <summary>Determines whether <paramref name="kind"/> is an at-rule.</summary>
    public static bool IsAtRule(SyntaxKind kind) => kind is >= SyntaxKind.CharsetRule and <= SyntaxKind.UnknownAtRule;

    /// <summary>Gets the closing token of a block opened by <paramref name="kind"/>, or <see cref="SyntaxKind.None"/>.</summary>
    public static SyntaxKind GetClosingKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.OpenParenToken or SyntaxKind.FunctionToken => SyntaxKind.CloseParenToken,
        SyntaxKind.OpenBracketToken => SyntaxKind.CloseBracketToken,
        SyntaxKind.OpenBraceToken => SyntaxKind.CloseBraceToken,
        _ => SyntaxKind.None,
    };

    /// <summary>Gets the kind of the at-rule named <paramref name="name"/> at the top level of a style sheet or in a block that is not specific to it.</summary>
    /// <param name="name">The name without the <c>@</c>, compared without regard to ASCII case.</param>
    /// <returns>
    /// The kind of the rule, or <see cref="SyntaxKind.UnknownAtRule"/>. A page margin rule such as <c>@top-left</c> and
    /// a feature block such as <c>@swash</c> are only known inside the rule they belong to, so they are unknown here.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static SyntaxKind GetAtRuleKind(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return CssIdentifier.ToAsciiLowerCase(name) switch
        {
            "charset" => SyntaxKind.CharsetRule,
            "import" => SyntaxKind.ImportRule,
            "namespace" => SyntaxKind.NamespaceRule,
            "media" => SyntaxKind.MediaRule,
            "supports" => SyntaxKind.SupportsRule,
            "container" => SyntaxKind.ContainerRule,
            "layer" => SyntaxKind.LayerRule,
            "scope" => SyntaxKind.ScopeRule,
            "starting-style" => SyntaxKind.StartingStyleRule,
            "keyframes" or "-webkit-keyframes" or "-moz-keyframes" or "-o-keyframes" => SyntaxKind.KeyframesRule,
            "font-face" => SyntaxKind.FontFaceRule,
            "page" => SyntaxKind.PageRule,
            "property" => SyntaxKind.PropertyRule,
            "counter-style" => SyntaxKind.CounterStyleRule,
            "font-feature-values" => SyntaxKind.FontFeatureValuesRule,
            "font-palette-values" => SyntaxKind.FontPaletteValuesRule,
            "position-try" => SyntaxKind.PositionTryRule,
            "view-transition" => SyntaxKind.ViewTransitionRule,
            "custom-media" => SyntaxKind.CustomMediaRule,
            "function" => SyntaxKind.FunctionRule,
            _ => SyntaxKind.UnknownAtRule,
        };
    }

    /// <summary>Determines whether <paramref name="name"/> is a page margin rule, such as <c>top-left</c>, which <c>@page</c> may hold.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsPageMarginRuleName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return CssIdentifier.ToAsciiLowerCase(name) is "top-left-corner" or "top-left" or "top-center" or "top-right" or "top-right-corner"
            or "bottom-left-corner" or "bottom-left" or "bottom-center" or "bottom-right" or "bottom-right-corner"
            or "left-top" or "left-middle" or "left-bottom" or "right-top" or "right-middle" or "right-bottom";
    }

    /// <summary>Determines whether <paramref name="name"/> is a feature block, such as <c>swash</c>, which <c>@font-feature-values</c> may hold.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsFontFeatureValueBlockName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return CssIdentifier.ToAsciiLowerCase(name) is "stylistic" or "historical-forms" or "styleset" or "character-variant" or "swash" or "ornaments" or "annotation";
    }

    /// <summary>Determines whether <paramref name="name"/> is a custom property name: two dashes followed by anything.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsCustomPropertyName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.StartsWith("--", StringComparison.Ordinal);
    }

    /// <summary>Determines whether <paramref name="name"/> is vendor-prefixed, such as <c>-webkit-scrollbar</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsVendorPrefixed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length < 3 || name[0] != '-' || name[1] == '-')
            return false;

        return name.AsSpan(2).Contains('-');
    }

    /// <summary>Determines whether <paramref name="name"/> is a pseudo-class written without arguments, such as <c>hover</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsKnownPseudoClass(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return KnownNames.PseudoClasses.Contains(name);
    }

    /// <summary>Determines whether <paramref name="name"/> is a pseudo-class written as a function, such as <c>is</c> or <c>nth-child</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsKnownFunctionalPseudoClass(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return KnownNames.FunctionalPseudoClasses.Contains(name);
    }

    /// <summary>Determines whether <paramref name="name"/> is a pseudo-element written without arguments, such as <c>before</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsKnownPseudoElement(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return KnownNames.PseudoElements.Contains(name);
    }

    /// <summary>Determines whether <paramref name="name"/> is a pseudo-element written as a function, such as <c>part</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsKnownFunctionalPseudoElement(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return KnownNames.FunctionalPseudoElements.Contains(name);
    }

    /// <summary>Determines whether <paramref name="name"/> is one of the four pseudo-elements CSS 2 wrote with a single colon.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsLegacyPseudoElement(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return CssIdentifier.ToAsciiLowerCase(name) is "before" or "after" or "first-line" or "first-letter";
    }

    /// <summary>Determines whether <paramref name="name"/> is a keyword every property accepts, such as <c>inherit</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsCssWideKeyword(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return CssIdentifier.ToAsciiLowerCase(name) is "initial" or "inherit" or "unset" or "revert" or "revert-layer" or "default";
    }

    /// <summary>Determines whether <paramref name="name"/> is an identifier that needs no escaping in CSS.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool IsValidIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return CssIdentifier.IsValidIdentifier(name);
    }
}
