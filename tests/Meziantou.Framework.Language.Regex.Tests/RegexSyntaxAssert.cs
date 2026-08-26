namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>Checks that a parsed tree accounts for every character of its source.</summary>
/// <remarks>
/// The root node keeps the original text, so <c>Root.ToFullString()</c> round-trips even when a child dropped a
/// character or claimed the wrong span. What catches that is comparing every node and token against the slice its span
/// points at, and then walking the tokens in order to confirm they tile the source with no gap and no overlap.
/// </remarks>
internal static class RegexSyntaxAssert
{
    public static RegexSyntaxTree TextIsFaithful(string text, RegexFlavor flavor)
    {
        var tree = RegexSyntaxTree.ParseText(text, flavor);
        TextIsFaithful(text, tree);

        return tree;
    }

    public static RegexSyntaxTree TextIsFaithful(string text, RegexParseOptions options)
    {
        var tree = RegexSyntaxTree.ParseText(text, options);
        TextIsFaithful(text, tree);

        return tree;
    }

    /// <summary>Runs the checks, naming the pattern that failed so a fuzz failure is reproducible.</summary>
    public static void TextIsFaithful(string text, RegexSyntaxTree tree)
    {
        try
        {
            Verify(text, tree);
        }
        catch (Exception exception)
        {
            Assert.Fail($"[{text}] as {tree.Flavor} with {tree.PatternOptions}: {exception.Message}");
        }
    }

    private static void Verify(string text, RegexSyntaxTree tree)
    {
        Assert.Equal(text, tree.Text);
        Assert.Equal(text, tree.Root.ToFullString());

        foreach (var node in tree.Root.DescendantNodesAndSelf())
        {
            var span = node.FullSpan;
            var insideSource = span.Start >= 0 && span.End <= text.Length;
            Assert.True(insideSource, $"{node.Kind} has span {span} outside a source of length {text.Length}.");
            Assert.Equal(text[span.Start..span.End], node.ToFullString());

            if (node.Parent is { } parent)
            {
                Assert.True(
                    parent.FullSpan.Start <= span.Start && span.End <= parent.FullSpan.End,
                    $"{node.Kind} {span} escapes its parent {parent.Kind} {parent.FullSpan}.");
            }
        }

        var position = 0;
        foreach (var token in tree.Root.DescendantTokens())
        {
            var insideSource = token.FullSpan.End <= text.Length;
            Assert.True(insideSource, $"{token.Kind} has span {token.FullSpan} outside a source of length {text.Length}.");

            foreach (var trivia in token.LeadingTrivia)
            {
                position = AssertClaims(text, position, trivia.Span, trivia.Text, trivia.Kind);
            }

            position = AssertClaims(text, position, token.Span, token.Text, token.Kind);

            foreach (var trivia in token.TrailingTrivia)
            {
                position = AssertClaims(text, position, trivia.Span, trivia.Text, trivia.Kind);
            }
        }

        // Everything the source contains has to be claimed by exactly one token or one piece of trivia.
        Assert.Equal(text.Length, position);

        foreach (var diagnostic in tree.Diagnostics)
        {
            var spanIsInsideSource = diagnostic.Span.Start >= 0 && diagnostic.Span.End <= text.Length;
            Assert.True(spanIsInsideSource, $"{diagnostic.Id} has span {diagnostic.Span} outside a source of length {text.Length}.");
            Assert.NotEmpty(diagnostic.Message);
            Assert.StartsWith("REGEX".AsSpan(), diagnostic.Id.AsSpan());
        }
    }

    /// <summary>Asserts that the next piece of the source starts exactly where the previous one stopped.</summary>
    private static int AssertClaims(string text, int position, TextSpan span, string claimed, RegexSyntaxKind kind)
    {
        var startsWherePreviousStopped = span.Start == position;
        Assert.True(startsWherePreviousStopped, $"{kind} starts at {span.Start} but the previous piece stopped at {position}.");
        Assert.Equal(claimed.Length, span.Length);
        Assert.Equal(text[span.Start..span.End], claimed);

        return span.End;
    }
}
