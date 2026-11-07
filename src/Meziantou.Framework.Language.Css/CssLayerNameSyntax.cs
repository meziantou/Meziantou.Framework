namespace Meziantou.Framework.Language.Css;

/// <summary>Represents the name of a cascade layer, such as <c>framework.base</c>.</summary>
public sealed partial class CssLayerNameSyntax
{
    /// <summary>Gets the parts of the name, with their escapes resolved, such as <c>framework</c> and <c>base</c>.</summary>
    public IReadOnlyList<string> Parts => [.. Tokens.Where(token => token.IsKind(SyntaxKind.IdentToken)).Select(token => token.ValueText)];

    /// <summary>Gets the whole name, its parts joined by dots.</summary>
    public string Name => string.Join('.', Parts);
}
