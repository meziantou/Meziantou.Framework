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
    public void RemoveNode_TakesTheSeparatorWithIt()
    {
        var root = TestSyntax.ParseRoot("(a,b)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("(b)", updated.ToFullString());
        Assert.Equal(1, Assert.IsType<TestListSyntax>(updated.Value).Values.Count);
    }

    [Fact]
    public void RemoveNode_OfTheLastElement_TakesTheSeparatorBeforeIt()
    {
        var root = TestSyntax.ParseRoot("(a,b)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.RemoveNode(values[1], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("(a)", updated.ToFullString());
    }

    [Fact]
    public void RemoveNode_OfTheOnlyElement_EmptiesTheList()
    {
        var root = TestSyntax.ParseRoot("(a)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("()", updated.ToFullString());
        Assert.Equal(0, Assert.IsType<TestListSyntax>(updated.Value).Values.Count);
    }

    [Fact]
    public void RemoveNode_LeavesTriviaThatBelongedToTheTokensAroundIt()
    {
        var root = TestSyntax.ParseRoot("( a )");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepNoTrivia);

        // Both spaces sit on the parentheses -- one trails "(" and the other trails "a" -- so only the second goes.
        Assert.Equal("( )", updated.ToFullString());
    }

    [Fact]
    public void RemoveNode_KeepingLeadingTrivia_MovesItOntoWhatFollows()
    {
        var root = TestSyntax.ParseRoot("(\n  a,\n  b\n)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var kept = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepLeadingTrivia);
        var dropped = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepNoTrivia);

        // The two spaces that were in front of "a" end up in front of "b".
        Assert.Equal("(\n    b\n)", kept.ToFullString());
        Assert.Equal("(\n  b\n)", dropped.ToFullString());
    }

    [Fact]
    public void RemoveNode_KeepingLeadingTrivia_MovesItOntoWhatPrecedesWhenNothingFollows()
    {
        var root = TestSyntax.ParseRoot("(a,\n  b)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var kept = root.RemoveNode(values[1], SyntaxRemoveOptions.KeepLeadingTrivia);
        var dropped = root.RemoveNode(values[1], SyntaxRemoveOptions.KeepNoTrivia);

        // Nothing follows "b", so the indentation in front of it ends up after "a" instead.
        Assert.Equal("(a  )", kept.ToFullString());
        Assert.Equal("(a)", dropped.ToFullString());
    }

    [Fact]
    public void RemoveNode_KeepingTrailingTrivia_KeepsWhatCameAfterIt()
    {
        var root = TestSyntax.ParseRoot("(a , b)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepTrailingTrivia);

        // The space after "a" and the one after the comma both survive.
        Assert.Equal("(  b)", updated.ToFullString());
    }

    [Fact]
    public void RemoveNode_KeepingEndOfLine_LeavesTheLineBreakBehind()
    {
        var root = TestSyntax.ParseRoot("(a,\nb)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var kept = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepEndOfLine);
        var dropped = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("(\nb)", kept.ToFullString());
        Assert.Equal("(b)", dropped.ToFullString());
    }

    [Fact]
    public void RemoveNode_KeepingEndOfLine_AddsNothingWhenAnotherOptionAlreadyKeptOne()
    {
        var root = TestSyntax.ParseRoot("(a,\nb)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var both = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepTrailingTrivia | SyntaxRemoveOptions.KeepEndOfLine);
        var trailingOnly = root.RemoveNode(values[0], SyntaxRemoveOptions.KeepTrailingTrivia);

        Assert.Equal(trailingOnly.ToFullString(), both.ToFullString());
    }

    [Fact]
    public void RemoveNodes_TakesSeveralOutAtOnce()
    {
        var root = TestSyntax.ParseRoot("(a,b,c,d)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.RemoveNodes([values[0], values[2]], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("(b,d)", updated.ToFullString());
    }

    [Fact]
    public void RemoveNodes_TakingEveryElement_EmptiesTheList()
    {
        var root = TestSyntax.ParseRoot("(a,b,c)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.RemoveNodes(values.ToArray(), SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("()", updated.ToFullString());
    }

    [Fact]
    public void RemoveNodes_FromDifferentLists_AppliesBoth()
    {
        var root = TestSyntax.ParseRoot("((a,b),(c,d))");
        var outer = Assert.IsType<TestListSyntax>(root.Value).Values;
        var first = Assert.IsType<TestListSyntax>(outer[0]).Values[0];
        var second = Assert.IsType<TestListSyntax>(outer[1]).Values[1];

        var updated = root.RemoveNodes([first, second], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("((b),(c))", updated.ToFullString());
    }

    [Fact]
    public void RemoveNodes_OfANodeAndSomethingInsideIt_RemovesTheNodeOnce()
    {
        var root = TestSyntax.ParseRoot("((a,b),c)");
        var outer = Assert.IsType<TestListSyntax>(root.Value).Values;
        var inner = Assert.IsType<TestListSyntax>(outer[0]);

        var updated = root.RemoveNodes([inner, inner.Values[0]], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("(c)", updated.ToFullString());
    }

    [Fact]
    public void RemoveNodes_AtDifferentDepths_AppliesBoth()
    {
        var root = TestSyntax.ParseRoot("((a,b),c,d)");
        var outer = Assert.IsType<TestListSyntax>(root.Value).Values;
        var inner = Assert.IsType<TestListSyntax>(outer[0]);

        // One removal is inside a node the other removal is a sibling of, so both have to survive the same rebuild.
        var updated = root.RemoveNodes([inner.Values[1], outer[1]], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("((a),d)", updated.ToFullString());
    }

    [Fact]
    public void RemoveNode_LeavesSpansConsistent()
    {
        var root = TestSyntax.ParseRoot("( a , (b, c) , d )");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.RemoveNode(values[1], SyntaxRemoveOptions.KeepNoTrivia);
        var text = updated.ToFullString();

        foreach (var token in updated.DescendantTokens())
        {
            Assert.Equal(token.Text, text.Substring(token.Span.Start, token.Span.Length));
        }
    }

    [Fact]
    public void RemoveNode_OfANodeFromAnotherTree_SaysSo()
    {
        var root = TestSyntax.ParseRoot("(a)");
        var stranger = Assert.IsType<TestListSyntax>(TestSyntax.ParseRoot("(b)").Value).Values[0];

        var exception = Assert.Throws<ArgumentException>(() => root.RemoveNode(stranger, SyntaxRemoveOptions.KeepNoTrivia));

        Assert.Equal("nodes", exception.ParamName);
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

    /// <summary>
    /// Two elements that are the same immutable node are still two elements. Locating one by the node behind it
    /// would always answer with the first, because that node is shared.
    /// </summary>
    [Fact]
    public void SyntaxList_LocatesARepeatedElementByWhichOneItIs()
    {
        var atom = TestSyntax.Atom("a");
        var list = new SyntaxList<TestValueSyntax>([atom, atom]);

        Assert.Equal(0, list.IndexOf(list[0]));
        Assert.Equal(1, list.IndexOf(list[1]));
    }

    [Fact]
    public void SyntaxList_RemoveAndReplaceTakeTheElementTheyWereGiven()
    {
        var atom = TestSyntax.Atom("a");
        var list = new SyntaxList<TestValueSyntax>([atom, atom, TestSyntax.Atom("b")]);

        var removed = list.Remove(list[1]);
        Assert.Equal(["a", "b"], removed.Select(value => value.ToFullString()));

        var replaced = list.Replace(list[1], TestSyntax.Atom("z"));
        Assert.Equal(["a", "z", "b"], replaced.Select(value => value.ToFullString()));
    }

    [Fact]
    public void SyntaxTokenList_LocatesARepeatedTokenByWhichOneItIs()
    {
        var comma = TestSyntax.Atom("a").IdentifierToken;
        var list = new SyntaxTokenList([comma, comma]);

        Assert.Equal(0, list.IndexOf(list[0]));
        Assert.Equal(1, list.IndexOf(list[1]));
    }

    [Fact]
    public void SyntaxTriviaList_LocatesARepeatedTriviumByWhichOneItIs()
    {
        var list = TestSyntax.ParseRoot("  a").Value!.GetLeadingTrivia();
        var doubled = list.Add(list[0]);

        Assert.Equal(0, doubled.IndexOf(doubled[0]));
        Assert.Equal(1, doubled.IndexOf(doubled[1]));
    }

    [Fact]
    public void SyntaxNodeOrTokenList_LocatesARepeatedItemByWhichOneItIs()
    {
        var atom = TestSyntax.Atom("a");
        var list = new SyntaxNodeOrTokenList([atom, atom]);

        Assert.Equal(0, list.IndexOf(list[0]));
        Assert.Equal(1, list.IndexOf(list[1]));
    }

    /// <summary>
    /// A batch naming one node in the tree and one from somewhere else is rejected, the way a single foreign node is.
    /// Replacing what it can and ignoring the rest would leave the caller believing the whole batch had applied.
    /// </summary>
    [Fact]
    public void ReplaceNodes_RejectsABatchHoldingANodeFromAnotherTree()
    {
        var root = TestSyntax.ParseRoot("(a, b)");
        var mine = root.Value!.DescendantNodes().OfType<TestAtomSyntax>().First();
        var foreign = TestSyntax.ParseRoot("(x)").Value!.DescendantNodes().OfType<TestAtomSyntax>().First();

        Assert.Throws<ArgumentException>(() => root.ReplaceNodes<TestRootSyntax, TestValueSyntax>([mine, foreign], (_, _) => TestSyntax.Atom("z")));
    }

    [Fact]
    public void ReplaceTokens_RejectsABatchHoldingATokenFromAnotherTree()
    {
        var root = TestSyntax.ParseRoot("(a, b)");
        var mine = root.DescendantTokens().First();
        var foreign = TestSyntax.ParseRoot("(x)").DescendantTokens().First();

        Assert.Throws<ArgumentException>(() => root.ReplaceTokens([mine, foreign], (_, _) => mine));
    }

    /// <summary>
    /// A list with no separators has nothing standing between its elements, so nothing goes with a removed one. The
    /// bracketed form of the toy language is that list; the parenthesised form is the separated one.
    /// </summary>
    [Theory]
    [InlineData(0, "[b c]")]
    [InlineData(1, "[a c]")]
    [InlineData(2, "[a b ]")]
    public void RemoveNode_FromAListWithNoSeparators_TakesOnlyThatNode(int index, string expected)
    {
        var root = TestSyntax.ParseRoot("[a b c]");
        var values = Assert.IsType<TestBlockSyntax>(root.Value).Values;

        var updated = root.RemoveNode(values[index], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal(expected, updated.ToFullString());
        Assert.Equal(2, Assert.IsType<TestBlockSyntax>(updated.Value).Values.Count);
    }

    [Fact]
    public void InsertNodesAfter_InAListWithNoSeparators_AddsNoSeparator()
    {
        var root = TestSyntax.ParseRoot("[a]");
        var values = Assert.IsType<TestBlockSyntax>(root.Value).Values;

        var updated = root.InsertNodesAfter(values[0], [TestSyntax.Atom("b")]);

        Assert.Equal("[ab]", updated.ToFullString());
    }

    /// <summary>
    /// A separated list of one element holds no separator yet, so what kind of list it is cannot be read off its
    /// contents. Getting that wrong puts two elements next to each other with nothing between them.
    /// </summary>
    [Fact]
    public void InsertNodesAfter_InASeparatedListOfOneElement_StillAddsTheSeparator()
    {
        var root = TestSyntax.ParseRoot("(a)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.InsertNodesAfter(values[0], [TestSyntax.Atom("b")]);

        Assert.Equal("(a,b)", updated.ToFullString());
        Assert.Equal(2, Assert.IsType<TestListSyntax>(updated.Value).Values.Count);
    }

    [Fact]
    public void ReplaceNode_WithSeveralNodes_InASeparatedListOfOneElement_StillAddsTheSeparator()
    {
        var root = TestSyntax.ParseRoot("(a)");
        var values = Assert.IsType<TestListSyntax>(root.Value).Values;

        var updated = root.ReplaceNode(values[0], [TestSyntax.Atom("b"), TestSyntax.Atom("c")]);

        Assert.Equal("(b,c)", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNodes_WithNothingToReplace_GivesTheRootBack()
    {
        var root = TestSyntax.ParseRoot("(a,b)");

        var updated = root.ReplaceNodes<TestRootSyntax, TestValueSyntax>([], (_, _) => TestSyntax.Atom("z"));

        Assert.Same(root, updated);
    }

    [Fact]
    public void ReplaceTokens_WithNothingToReplace_GivesTheRootBack()
    {
        var root = TestSyntax.ParseRoot("(a,b)");

        var updated = root.ReplaceTokens([], (_, _) => default);

        Assert.Same(root, updated);
    }

    [Fact]
    public void ReplaceTrivia_WithNothingToReplace_GivesTheRootBack()
    {
        var root = TestSyntax.ParseRoot("( a , b )");

        var updated = root.ReplaceTrivia([], (_, _) => default);

        Assert.Same(root, updated);
    }

    /// <summary>
    /// Rebuilding walks the spine from the root down to what changed, which in a deeply nested document is a long way.
    /// </summary>
    [Fact]
    public void ReplaceNode_InADeeplyNestedDocument_DoesNotOverflowTheStack()
    {
        const int Depth = 10_000;

        // Built from the inside out rather than parsed, so this measures the shared layer and not the toy parser.
        var root = (TestRootSyntax)BuildNesting(Depth).CreateRed();
        var innermost = root.DescendantNodes().OfType<TestAtomSyntax>().Single();

        var updated = root.ReplaceNode(innermost, TestSyntax.Atom("b"));

        Assert.Equal(new string('(', Depth) + "b" + new string(')', Depth), updated.ToFullString());

        static InternalSyntax.GreenNode BuildNesting(int depth)
        {
            InternalSyntax.GreenNode green = new TestGreen.Atom(TestGreen.Token(TestSyntaxKind.IdentifierToken, "a"));
            for (var i = 0; i < depth; i++)
            {
                green = new TestGreen.List(TestGreen.Token(TestSyntaxKind.OpenParenToken, "("), green, TestGreen.Token(TestSyntaxKind.CloseParenToken, ")"));
            }

            return new TestGreen.Root(green, TestGreen.Token(TestSyntaxKind.EndOfFileToken, ""));
        }
    }

    /// <summary>
    /// A slot that holds a single node is not a list, even though the only element of a list looks exactly like one.
    /// </summary>
    [Fact]
    public void InsertNodesAfter_WhereOnlyOneNodeFits_IsRejected()
    {
        var root = TestSyntax.ParseRoot("(a)");
        var only = Assert.IsType<TestListSyntax>(root.Value).Values[0];

        Assert.Throws<ArgumentException>(() => root.InsertNodesAfter(root.Value!, [TestSyntax.Atom("b")]));
        Assert.Throws<ArgumentException>(() => root.ReplaceNode(root.Value!, [TestSyntax.Atom("b")]));

        // The only element of a list is still in a list, and takes as many nodes as ever.
        Assert.Equal("(a,b)", root.InsertNodesAfter(only, [TestSyntax.Atom("b")]).ToFullString());
    }
}
