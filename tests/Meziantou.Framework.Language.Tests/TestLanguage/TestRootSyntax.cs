using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Tests.TestLanguage;

/// <summary>A whole document: one value and the end of the text.</summary>
internal sealed class TestRootSyntax : TestSyntaxNode
{
    private TestValueSyntax? _value;

    internal TestRootSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public TestValueSyntax? Value => GetRedAtZero(ref _value);

    public SyntaxToken EndOfFileToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    internal override SyntaxNode? GetNodeSlot(int index) => index == 0 ? GetRedAtZero(ref _value) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 0 ? _value : null;
}
