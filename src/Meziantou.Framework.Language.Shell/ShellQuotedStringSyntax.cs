namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a quoted section of a word.</summary>
public sealed partial class ShellQuotedStringSyntax
{
    /// <summary>Returns <see langword="true"/> for single-quoted text, where neither expansion nor escaping happens.</summary>
    public bool IsVerbatim => OpenQuoteToken.Kind() == SyntaxKind.SingleQuoteToken;

    /// <summary>Returns <see langword="true"/> for the bash <c>$'...'</c> form, which resolves ANSI-C escapes.</summary>
    public bool IsAnsiC => OpenQuoteToken.Kind() == SyntaxKind.DollarSingleQuoteToken;
}
