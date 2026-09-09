using Meziantou.Framework.Language.Tests.TestLanguage;

namespace Meziantou.Framework.Language.Tests;

/// <summary>
/// Editing rebuilds only the spine from the changed node up to the root; everything else is carried over unchanged.
/// </summary>
public sealed class SyntaxReplaceTests
{
    [Fact]
    public void ReplaceNode_KeepsTheTypeOfTheRoot()
    {
        var root = TestSyntax.ParseRoot("(a,b)");
        var target = Assert.IsType<TestListSyntax>(root.Value).Values[1];

        // No cast: the result is known to be a root because that is what was passed in.
        TestRootSyntax updated = root.ReplaceNode(target, TestSyntax.Atom("z"));

        Assert.Equal("(a,z)", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_LeavesTheOriginalTreeAlone()
    {
        var root = TestSyntax.ParseRoot("(a,b)");
        var target = Assert.IsType<TestListSyntax>(root.Value).Values[1];

        root.ReplaceNode(target, TestSyntax.Atom("z"));

        Assert.Equal("(a,b)", root.ToFullString());
    }

    [Fact]
    public void ReplaceNode_SharesEverythingThatDidNotChange()
    {
        var root = TestSyntax.ParseRoot("((a,b),(c,d))");
        var outer = Assert.IsType<TestListSyntax>(root.Value);
        var untouched = Assert.IsType<TestListSyntax>(outer.Values[0]);
        var target = Assert.IsType<TestListSyntax>(outer.Values[1]).Values[0];

        var updated = root.ReplaceNode(target, TestSyntax.Atom("z"));

        Assert.Equal("((a,b),(z,d))", updated.ToFullString());

        var updatedOuter = Assert.IsType<TestListSyntax>(updated.Value);
        Assert.True(untouched.IsIncrementallyIdenticalTo(updatedOuter.Values[0]), "The untouched sibling should be the very same immutable node.");
        Assert.False(root.IsIncrementallyIdenticalTo(updated), "The spine up to the root has to be rebuilt.");
    }

    [Fact]
    public void ReplaceNode_MovesTheFollowingNodesWhenTheReplacementIsALongerText()
    {
        var root = TestSyntax.ParseRoot("(a,b,c)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.ReplaceNode(values[0], TestSyntax.Atom("longer"));
        var updatedValues = Assert.IsType<TestListSyntax>(updated.Value).Values;

        Assert.Equal("(longer,b,c)", updated.ToFullString());
        Assert.Equal("(longer,b,c)".IndexOf('c', StringComparison.Ordinal), updatedValues[2].SpanStart);
    }

    [Fact]
    public void ReplaceNode_OfANodeFromAnotherTree_SaysSo()
    {
        var root = TestSyntax.ParseRoot("(a)");
        var stranger = Assert.IsType<TestListSyntax>(TestSyntax.ParseRoot("(b)").Value).Values[0];

        var exception = Assert.Throws<ArgumentException>(() => root.ReplaceNode(stranger, TestSyntax.Atom("z")));

        Assert.Equal("nodes", exception.ParamName);
    }

    [Fact]
    public void ReplaceNodes_ReplacesSeveralAtOnce()
    {
        var root = TestSyntax.ParseRoot("(a,b,c)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.ReplaceNodes(values.ToArray(), (original, _) => TestSyntax.Atom(((TestAtomSyntax)original).Name.ToUpperInvariant()));

        Assert.Equal("(A,B,C)", updated.ToFullString());
    }

    [Fact]
    public void ReplaceToken_RebuildsWithoutTouchingTheSurroundingTrivia()
    {
        var root = TestSyntax.ParseRoot("( a , b )");
        var target = Assert.IsType<TestAtomSyntax>(Assert.IsType<TestListSyntax>(root.Value).Values[0]).IdentifierToken;

        var replacement = TestSyntax.Atom("z").IdentifierToken.WithTriviaFrom(target);
        var updated = root.ReplaceToken(target, replacement);

        Assert.Equal("( z , b )", updated.ToFullString());
    }

    [Fact]
    public void ReplaceTrivia_ChangesOnlyThatTrivia()
    {
        var root = TestSyntax.ParseRoot("( a )");
        var trivia = root.GetFirstToken().TrailingTrivia[0];

        var updated = root.ReplaceTrivia(trivia, TestSyntax.ParseRoot("   a").GetFirstToken().LeadingTrivia[0]);

        Assert.Equal("(   a )", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_WithSeveralNodes_SplicesThemIntoTheList()
    {
        var root = TestSyntax.ParseRoot("(a,b)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.ReplaceNode(values[0], [TestSyntax.Atom("x"), TestSyntax.Atom("y")]);

        // The spliced nodes go in where the old one was, with the separators the list needs to stay alternating.
        Assert.Equal("(x,y,b)", updated.ToFullString());
    }

    [Fact]
    public void InsertNodesBeforeAndAfter_PutThemOnTheRightSide()
    {
        var root = TestSyntax.ParseRoot("(a,b)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var before = root.InsertNodesBefore(values[0], [TestSyntax.Atom("x")]);
        var after = root.InsertNodesAfter(values[0], [TestSyntax.Atom("x")]);

        Assert.Equal("(x,a,b)", before.ToFullString());
        Assert.Equal("(a,x,b)", after.ToFullString());
    }

    [Fact]
    public void RemoveNode_TakesItOutOfTheList()
    {
        var root = TestSyntax.ParseRoot("(a,b)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.RemoveNode(values[0]);

        Assert.Equal("(b)", updated.ToFullString());
        Assert.Equal(1, Assert.IsType<TestListSyntax>(updated.Value).Values.Count);
    }

    [Fact]
    public void AnAnnotationOnASibling_SurvivesAnEditElsewhere()
    {
        var annotation = new SyntaxAnnotation("marker");
        var root = TestSyntax.ParseRoot("(a,b)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var marked = root.ReplaceNode(values[0], values[0].WithAdditionalAnnotations(annotation));
        var markedValues = Assert.IsType<TestListSyntax>(marked.Value).Values;
        var edited = marked.ReplaceNode(markedValues[1], TestSyntax.Atom("z"));

        Assert.Equal("(a,z)", edited.ToFullString());
        Assert.Equal("a", edited.GetAnnotatedNodes(annotation).Single().ToString());
    }

    [Fact]
    public void SpansOfTheRebuiltTreeAreConsistent()
    {
        var root = TestSyntax.ParseRoot("( a , (b, c) , d )");
        var inner = Assert.IsType<TestListSyntax>(Assert.IsType<TestListSyntax>(root.Value).Values[1]);

        var updated = root.ReplaceNode(inner.Values[0], TestSyntax.Atom("much-longer".Replace("-", "", StringComparison.Ordinal)));
        var text = updated.ToFullString();

        foreach (var token in updated.DescendantTokens())
        {
            Assert.Equal(token.Text, text.Substring(token.Span.Start, token.Span.Length));
        }
    }
}
