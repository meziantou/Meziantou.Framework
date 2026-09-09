using Meziantou.Framework.Language.Tests.TestLanguage;

namespace Meziantou.Framework.Language.Tests;

/// <summary>
/// The properties every tree must have, whatever language produced it, checked against a toy language.
/// </summary>
/// <remarks>
/// Spans are computed by walking green widths and adding them up in several different places. These tests exist to
/// catch a disagreement between those places, which is otherwise invisible until something far away reports the
/// wrong position.
/// </remarks>
public sealed class SyntaxTreeInvariantTests
{
    public static TheoryData<string> Documents =>
    [
        "",
        "a",
        "  a  ",
        "(a)",
        "(a,b)",
        "(a,b,)",
        "( a , b )",
        "(a,(b,c),d)",
        "((((a))))",
        "(\n  a,\n  b\n)",
        "\n\n(a)\n\n",
        "(a,b,c,d,e,f,g,h,i,j,k,l)",
        "(a",
        "(a,",
        "()",
    ];

    [Theory]
    [MemberData(nameof(Documents))]
    public void ToFullString_ReproducesTheSourceText(string text)
    {
        Assert.Equal(text, TestSyntax.ParseRoot(text).ToFullString());
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void ChildFullSpans_TileTheParentExactly(string text)
    {
        foreach (var node in Descendants(TestSyntax.ParseRoot(text)))
        {
            var expectedStart = node.FullSpan.Start;
            var children = node.ChildNodesAndTokens();
            foreach (var child in children)
            {
                Assert.Equal(expectedStart, child.FullSpan.Start);
                expectedStart = child.FullSpan.End;
            }

            if (children.Count > 0)
            {
                Assert.Equal(node.FullSpan.End, expectedStart);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void SpanIsInsideFullSpan(string text)
    {
        foreach (var node in Descendants(TestSyntax.ParseRoot(text)))
        {
            Assert.True(node.FullSpan.Contains(node.Span), $"{node.Span} is not inside {node.FullSpan}.");
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void ConcatenatingEveryToken_ReproducesTheSourceText(string text)
    {
        var root = TestSyntax.ParseRoot(text);
        var builder = new StringBuilder();
        foreach (var token in Tokens(root))
        {
            builder.Append(token.ToFullString());
        }

        Assert.Equal(text, builder.ToString());
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryTokenReportsTheTextItsSpanCoversInTheSource(string text)
    {
        foreach (var token in Tokens(TestSyntax.ParseRoot(text)))
        {
            Assert.Equal(token.Text, text.Substring(token.Span.Start, token.Span.Length));
            Assert.Equal(token.ToFullString(), text.Substring(token.FullSpan.Start, token.FullSpan.Length));
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryTriviaReportsTheTextItsSpanCoversInTheSource(string text)
    {
        foreach (var token in Tokens(TestSyntax.ParseRoot(text)))
        {
            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                Assert.Equal(trivia.ToString(), text.Substring(trivia.Span.Start, trivia.Span.Length));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void WithSlots_RebuildingFromTheCurrentSlots_ProducesAnEquivalentNode(string text)
    {
        foreach (var node in Descendants(TestSyntax.ParseRoot(text)))
        {
            var green = node.Green;
            var slots = new InternalSyntax.GreenNode?[green.SlotCount];
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i] = green.GetSlot(i);
            }

            var rebuilt = green.WithSlots(slots);

            Assert.NotNull(rebuilt);
            Assert.True(green.IsEquivalentTo(rebuilt), $"Rebuilding a {green.KindText} from its own slots changed it.");
        }
    }

    [Fact]
    public void AskingForTheSameChildTwice_ReturnsTheSameInstance()
    {
        var root = TestSyntax.ParseRoot("(a,(b,c),d)");

        Assert.Same(root.Value, root.Value);

        var list = Assert.IsType<TestListSyntax>(root.Value);
        Assert.Same(list.Values[1], list.Values[1]);
        Assert.Same(list.Values[1], Assert.IsType<TestListSyntax>(list.Values[1]));
    }

    [Fact]
    public void TwoTreesOverTheSameImmutableNodes_DoNotShareTheirNodes()
    {
        var green = TestSyntax.ParseGreen("(a,b)");
        var first = (TestRootSyntax)green.CreateRed();
        var second = (TestRootSyntax)green.CreateRed();

        Assert.NotSame(first, second);
        Assert.NotSame(first.Value, second.Value);
        Assert.True(first.IsIncrementallyIdenticalTo(second));
        Assert.Same(first, first.Value!.Parent);
        Assert.Same(second, second.Value!.Parent);
    }

    [Fact]
    public void ADeeplyNestedDocument_DoesNotOverflowTheStack()
    {
        const int Depth = 10_000;
        var text = new string('(', Depth) + "a" + new string(')', Depth);

        var root = TestSyntax.ParseRoot(text);

        Assert.Equal(text, root.ToFullString());
        Assert.Equal(text.Length, root.FullSpan.Length);
    }

    [Fact]
    public void LeadingAndTrailingTrivia_SplitAtTheEndOfTheLine()
    {
        var root = TestSyntax.ParseRoot("(\n  a\n)");
        var list = Assert.IsType<TestListSyntax>(root.Value);

        // The line break belongs to the token that ends the line; the indentation belongs to the token that follows.
        Assert.Equal("\n", list.OpenParenToken.TrailingTrivia.ToFullString());
        Assert.Equal("  ", list.Values[0].GetLeadingTrivia().ToFullString());
        Assert.Equal("\n", list.Values[0].GetTrailingTrivia().ToFullString());
        Assert.Equal("", list.CloseParenToken.LeadingTrivia.ToFullString());
    }

    [Fact]
    public void SeparatedList_CountsSeparatorsSeparatelyFromElements()
    {
        var withTrailing = Assert.IsType<TestListSyntax>(TestSyntax.ParseRoot("(a,b,)").Value).Values;
        var withoutTrailing = Assert.IsType<TestListSyntax>(TestSyntax.ParseRoot("(a,b)").Value).Values;
        var single = Assert.IsType<TestListSyntax>(TestSyntax.ParseRoot("(a)").Value).Values;
        var empty = Assert.IsType<TestListSyntax>(TestSyntax.ParseRoot("()").Value).Values;

        Assert.Equal(2, withTrailing.Count);
        Assert.Equal(2, withTrailing.SeparatorCount);
        Assert.True(withTrailing.HasTrailingSeparator);

        Assert.Equal(2, withoutTrailing.Count);
        Assert.Equal(1, withoutTrailing.SeparatorCount);
        Assert.False(withoutTrailing.HasTrailingSeparator);

        Assert.Equal(1, single.Count);
        Assert.Equal(0, single.SeparatorCount);
        Assert.False(single.HasTrailingSeparator);

        Assert.Equal(0, empty.Count);
        Assert.Equal(0, empty.SeparatorCount);
        Assert.False(empty.HasTrailingSeparator);
    }

    [Fact]
    public void SeparatedList_ExposesElementsAndSeparatorsInSourceOrder()
    {
        var values = Assert.IsType<TestListSyntax>(TestSyntax.ParseRoot("(a, b, c)").Value).Values;

        Assert.Equal(["a", "b", "c"], values.Select(value => Assert.IsType<TestAtomSyntax>(value).Name));
        Assert.Equal([",", ","], values.GetSeparators().Select(separator => separator.Text));
        Assert.Equal("a, b, c", values.GetWithSeparators().ToFullString());
    }

    [Fact]
    public void AMissingToken_IsZeroWidthAndReportedAsMissing()
    {
        var list = Assert.IsType<TestListSyntax>(TestSyntax.ParseRoot("(a").Value);

        Assert.True(list.CloseParenToken.IsMissing);
        Assert.Equal(0, list.CloseParenToken.FullSpan.Length);
        Assert.Equal(2, list.CloseParenToken.FullSpan.Start);
        Assert.False(list.OpenParenToken.IsMissing);
    }

    private static IEnumerable<SyntaxNode> Descendants(SyntaxNode root)
    {
        var stack = new Stack<SyntaxNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            yield return node;

            foreach (var child in node.ChildNodesAndTokens())
            {
                if (child.AsNode(out var childNode))
                {
                    stack.Push(childNode);
                }
            }
        }
    }

    private static IEnumerable<SyntaxToken> Tokens(SyntaxNode root)
    {
        foreach (var child in root.ChildNodesAndTokens())
        {
            if (child.AsToken(out var token))
            {
                yield return token;
            }
            else if (child.AsNode(out var node))
            {
                foreach (var descendant in Tokens(node))
                {
                    yield return descendant;
                }
            }
        }
    }
}
