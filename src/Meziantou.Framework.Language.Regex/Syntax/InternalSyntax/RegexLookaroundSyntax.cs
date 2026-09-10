using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <remarks>The JavaScript parser asks which way a lookaround faces while it is still building the tree.</remarks>
internal sealed partial class RegexLookaroundSyntax
{
    /// <summary>Gets a value indicating whether the lookaround looks backwards, as <c>(?&lt;=…)</c> does.</summary>
    public bool IsLookbehind => GetSlot(1) is GreenToken token && token.Text.Contains('<', StringComparison.Ordinal);
}
