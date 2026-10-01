namespace Meziantou.Framework.Language.Css;

/// <summary>Represents the <c>{ ... }</c> block of a rule, holding declarations and nested rules side by side.</summary>
public sealed partial class CssBlockSyntax
{
    /// <summary>Gets the declarations of the block, in source order.</summary>
    public IEnumerable<CssDeclarationSyntax> Declarations => Statements.OfType<CssDeclarationSyntax>();

    /// <summary>Gets the rules nested in the block, in source order.</summary>
    public IEnumerable<CssRuleSyntax> Rules => Statements.OfType<CssRuleSyntax>();
}
