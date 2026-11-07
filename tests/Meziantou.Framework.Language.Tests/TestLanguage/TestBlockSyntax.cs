using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Tests.TestLanguage;

/// <summary>A bracketed run of values with nothing between them.</summary>
/// <remarks>
/// The counterpart of <see cref="TestListSyntax"/>: its values sit in a plain list rather than a separated one, which
/// is what an edit has to tell apart before it takes a neighbour out or puts a separator in.
/// </remarks>
internal sealed class TestBlockSyntax : TestValueSyntax
{
    private SyntaxNode? _values;

    internal TestBlockSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken OpenBracketToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    public SyntaxList<TestValueSyntax> Values => new(GetRed(ref _values, 1));

    public SyntaxToken CloseBracketToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    internal override SyntaxNode? GetNodeSlot(int index) => index == 1 ? GetRed(ref _values, 1) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 1 ? _values : null;
}
