using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Tests.TestLanguage;

/// <summary>Anything that can appear where the toy grammar expects a value.</summary>
internal abstract class TestValueSyntax : TestSyntaxNode
{
    protected TestValueSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
