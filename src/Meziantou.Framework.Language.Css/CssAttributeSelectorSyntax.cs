using Meziantou.Framework.Language.Css.Internals;

namespace Meziantou.Framework.Language.Css;

/// <summary>Represents an attribute selector, such as <c>[href^="https:" i]</c>.</summary>
public sealed partial class CssAttributeSelectorSyntax
{
    /// <summary>Gets the attribute name, with its escapes resolved.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Gets how the value of the attribute is compared.</summary>
    public CssAttributeOperator Operator => (EqualsToken.IsPresent(), OperatorPrefixToken.Kind()) switch
    {
        (false, _) => CssAttributeOperator.None,
        (true, SyntaxKind.TildeToken) => CssAttributeOperator.Includes,
        (true, SyntaxKind.BarToken) => CssAttributeOperator.DashMatch,
        (true, SyntaxKind.CaretToken) => CssAttributeOperator.Prefix,
        (true, SyntaxKind.DollarToken) => CssAttributeOperator.Suffix,
        (true, SyntaxKind.AsteriskToken) => CssAttributeOperator.Substring,
        _ => CssAttributeOperator.Equals,
    };

    /// <summary>Gets the value the attribute is compared with, unquoted and with its escapes resolved, or <see langword="null"/> when there is none.</summary>
    public string? Value => ValueToken.IsPresent() ? ValueToken.ValueText : null;

    /// <summary>
    /// Gets whether the value is compared without regard to ASCII case (<c>i</c>), with regard to it (<c>s</c>), or as
    /// the document language says (<see langword="null"/>).
    /// </summary>
    public bool? IsCaseInsensitive => ModifierToken.IsPresent() ? CssIdentifier.EqualsIgnoreAsciiCase(ModifierToken.ValueText, "i") : null;
}
