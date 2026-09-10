using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Tests.TestLanguage;

/// <summary>The base of every node of the toy language.</summary>
internal abstract class TestSyntaxNode : SyntaxNode
{
    protected TestSyntaxNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public TestSyntaxKind Kind() => (TestSyntaxKind)RawKind;
}
