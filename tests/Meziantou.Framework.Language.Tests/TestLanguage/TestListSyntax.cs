using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Tests.TestLanguage;

/// <summary>A parenthesised, comma-separated sequence of values.</summary>
internal sealed class TestListSyntax : TestValueSyntax
{
    private SyntaxNode? _values;

    internal TestListSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken OpenParenToken => new(this, Green.GetSlot(0), Position, index: 0);

    public SeparatedSyntaxList<TestValueSyntax> Values
    {
        get
        {
            var red = GetRed(ref _values, 1);

            return red is null ? default : new SeparatedSyntaxList<TestValueSyntax>(new SyntaxNodeOrTokenList(red, index: 1));
        }
    }

    public SyntaxToken CloseParenToken => new(this, Green.GetSlot(2), GetChildPosition(2), index: ChildNodesAndTokens().Count - 1);

    internal override SyntaxNode? GetNodeSlot(int index) => index == 1 ? GetRed(ref _values, 1) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 1 ? _values : null;
}
