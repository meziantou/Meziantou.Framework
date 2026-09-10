namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <remarks>The parser counts branches while it is still building the tree, before a red node exists to ask.</remarks>
internal sealed partial class RegexAlternationSyntax
{
    /// <summary>Gets the number of branches, which is every other entry of the alternating branch-and-bar list.</summary>
    public int BranchCount => GetSlot(0) switch
    {
        null => 0,
        { IsList: true } list => (list.SlotCount + 1) / 2,
        _ => 1,
    };
}
