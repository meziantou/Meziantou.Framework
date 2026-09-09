using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Tests.TestLanguage;

/// <summary>A single identifier.</summary>
internal sealed class TestAtomSyntax : TestValueSyntax
{
    internal TestAtomSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken IdentifierToken => new(this, Green.GetSlot(0), Position, index: 0);

    public string Name => IdentifierToken.ValueText;

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;
}
