using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// The properties every tree must have, whatever language produced it (tests/Meziantou.Framework.Language.Tests/SyntaxTreeInvariantTests.cs
/// and the <c>*SyntaxAssert</c> helpers of the language test projects).
/// </summary>
/// <remarks>
/// Every check is linear in the size of the text, or bounded: the root keeps the original text, so <c>ToFullString</c>
/// of the root round-trips even when a child drops a character, and what catches that is walking the tokens and their
/// trivia in order and comparing each one with the slice of the text its span points at. The checks that cost more per
/// item (<see cref="SyntaxNode.FindToken"/>, <see cref="SyntaxToken.GetNextToken"/>, which scan siblings) run on a sample
/// of positions and tokens. The walks use the iterative APIs, so a deeply nested tree cannot overflow the stack.
/// </remarks>
internal static class SyntaxTreeInvariants
{
    private const int SampleCount = 48;

    /// <summary>Checks the tree, and returns its tokens and the number of trivia they carry.</summary>
    /// <param name="tree">The tree parsed from <paramref name="text"/>.</param>
    /// <param name="text">The text.</param>
    /// <param name="diagnosticPrefix">The prefix of the identifier of every diagnostic of the language.</param>
    /// <param name="diagnosticsOnNodes">Whether the diagnostics are attached to the nodes, so that <see cref="SyntaxNode.ContainsDiagnostics"/> tells whether there are any.</param>
    /// <param name="diagnosticsInSourceOrder">Whether the language documents that the diagnostics are sorted by position.</param>
    public static TreeShape Check(SyntaxTree tree, string text, string diagnosticPrefix, bool diagnosticsOnNodes, bool diagnosticsInSourceOrder)
    {
        var root = tree.GetRoot();
        FuzzAssert.Equal(text, tree.GetText().Text, "The text of the tree is not the parsed text");
        FuzzAssert.Equal(text, tree.ToString(), "The tree does not round-trip");
        FuzzAssert.Equal(text, root.ToFullString(), "The root does not round-trip");
        FuzzAssert.Equal(new TextSpan(0, text.Length), root.FullSpan, "The root does not span the text");
        FuzzAssert.True(ReferenceEquals(root.SyntaxTree, tree), "The root does not belong to its tree");
        FuzzAssert.True(root.Parent is null, "The root has a parent");

        var tokens = new List<SyntaxToken>();
        var triviaCount = CheckTokens(root, text, tokens);
        CheckNodes(root, text, tokens);
        CheckNavigation(root, tree.GetText(), tokens);
        CheckDiagnostics(tree, text, diagnosticPrefix, diagnosticsOnNodes, diagnosticsInSourceOrder);
        return new TreeShape(tokens, triviaCount);
    }

    /// <summary>Checks that a walker of the language visited every token and every trivia of the tree.</summary>
    public static void CheckWalker(TreeShape shape, int tokenCount, int triviaCount)
    {
        FuzzAssert.Equal(shape.Tokens.Count, tokenCount, "The walker does not visit every token");
        FuzzAssert.Equal(shape.TriviaCount, triviaCount, "The walker does not visit every trivia");
    }

    /// <summary>Checks that every node and token of <paramref name="node"/> has the text its span points at in <paramref name="text"/>.</summary>
    /// <remarks>Used on the nodes the APIs return outside of a tree walk (<c>ParseCommand</c> of Shell, for instance).</remarks>
    public static void CheckSubtree(SyntaxNode node, string text)
    {
        var span = node.FullSpan;
        FuzzAssert.True(span.Start >= 0 && span.End <= text.Length, $"The node {node.RawKind} has the span {span} outside a text of {text.Length} characters");
        FuzzAssert.Equal(text[span.Start..span.End], node.ToFullString(), $"The node {node.RawKind} at {span} does not have the text of its span");
        var builder = new StringBuilder(span.Length);
        foreach (var token in node.DescendantTokens())
        {
            builder.Append(token.ToFullString());
        }

        FuzzAssert.Equal(text[span.Start..span.End], builder.ToString(), $"The tokens of the node {node.RawKind} at {span} do not have the text of its span");
    }

    private static int CheckTokens(SyntaxNode root, string text, List<SyntaxToken> tokens)
    {
        var position = 0;
        var triviaCount = 0;
        foreach (var token in root.DescendantTokens())
        {
            tokens.Add(token);
            var fullStart = position;
            foreach (var trivia in token.LeadingTrivia)
            {
                position = Claim(text, position, trivia.Span, trivia.ToString(), $"leading trivia {trivia.RawKind} of the token {token.RawKind}");
                FuzzAssert.True(trivia.Token == token, $"The leading trivia at {trivia.Span} does not belong to its token");
                triviaCount++;
            }

            FuzzAssert.Equal(position, token.SpanStart, $"The token {token.RawKind} does not start after its leading trivia");
            position = Claim(text, position, token.Span, token.Text, $"token {token.RawKind}");
            foreach (var trivia in token.TrailingTrivia)
            {
                position = Claim(text, position, trivia.Span, trivia.ToString(), $"trailing trivia {trivia.RawKind} of the token {token.RawKind}");
                FuzzAssert.True(trivia.Token == token, $"The trailing trivia at {trivia.Span} does not belong to its token");
                triviaCount++;
            }

            FuzzAssert.Equal(TextSpan.FromBounds(fullStart, position), token.FullSpan, $"The full span of the token {token.RawKind} is not its trivia and its text");
            FuzzAssert.True(token.FullSpan.Contains(token.Span), $"The span of the token {token.RawKind} is outside its full span");
            FuzzAssert.True(token.Parent is not null, $"The token {token.RawKind} at {token.Span} has no parent");
            if (token.IsMissing)
            {
                FuzzAssert.Equal(0, token.Span.Length, $"The missing token {token.RawKind} at {token.Span} has a width");
            }

            _ = token.ValueText;
            _ = token.Value;
        }

        FuzzAssert.Equal(text.Length, position, "The tokens do not cover the whole text");
        FuzzAssert.True(tokens.Count > 0, "The tree has no token (not even an end-of-file token)");
        return triviaCount;
    }

    /// <summary>Asserts that the next piece of the text starts exactly where the previous one stopped and has the text of its span.</summary>
    private static int Claim(string text, int position, TextSpan span, string claimed, string what)
    {
        FuzzAssert.True(span.Start == position, $"The {what} starts at {span.Start} but the previous piece stopped at {position}");
        FuzzAssert.True(span.End <= text.Length, $"The {what} has the span {span} outside a text of {text.Length} characters");
        FuzzAssert.Equal(span.Length, claimed.Length, $"The span of the {what} does not have the length of its text");
        FuzzAssert.True(text.AsSpan(span.Start, span.Length).SequenceEqual(claimed), $"The {what} at {span} is {FuzzAssert.Format(claimed)}, not the text of its span {FuzzAssert.Format(text[span.Start..span.End])}");
        return span.End;
    }

    /// <summary>
    /// Walks the nodes with an explicit stack, checking that the children of each node tile it, and that its span goes from
    /// the span start of its first token to the span end of its last one.
    /// </summary>
    private static void CheckNodes(SyntaxNode root, string text, List<SyntaxToken> tokens)
    {
        // Comparing the text of every node with its slice costs the depth of the tree for each character, so it stops
        // once it has compared a few times the length of the text; the tiling of the children checks the rest.
        var budget = (4L * text.Length) + 256;
        var tokenIndex = 0;
        var stack = new Stack<NodeFrame>();
        stack.Push(EnterNode(root, text, tokenIndex, ref budget));
        while (stack.TryPeek(out var frame))
        {
            if (frame.Children.MoveNext())
            {
                var child = frame.Children.Current;
                FuzzAssert.Equal(frame.NextChildStart, child.FullSpan.Start, $"A child {child.RawKind} of the node {frame.Node.RawKind} at {frame.Node.FullSpan} does not start where the previous one stopped");
                FuzzAssert.True(ReferenceEquals(child.Parent, frame.Node), $"A child {child.RawKind} of the node {frame.Node.RawKind} at {frame.Node.FullSpan} has another parent");
                frame.NextChildStart = child.FullSpan.End;
                frame.ChildCount++;
                if (child.AsNode(out var childNode))
                {
                    stack.Push(EnterNode(childNode, text, tokenIndex, ref budget));
                }
                else
                {
                    FuzzAssert.True(tokenIndex < tokens.Count && child.AsToken() == tokens[tokenIndex], $"The child token {child.RawKind} at {child.FullSpan} is not the token {tokenIndex} of DescendantTokens");
                    tokenIndex++;
                }

                continue;
            }

            stack.Pop();
            var node = frame.Node;
            var fullSpan = node.FullSpan;
            FuzzAssert.Equal(node.ChildNodesAndTokens().Count, frame.ChildCount, $"The node {node.RawKind} enumerates a different number of children than it counts");
            if (frame.ChildCount > 0)
            {
                FuzzAssert.Equal(fullSpan.End, frame.NextChildStart, $"The children of the node {node.RawKind} at {fullSpan} do not end where it does");
            }
            else
            {
                FuzzAssert.Equal(0, fullSpan.Length, $"The node {node.RawKind} at {fullSpan} has a width but no children");
            }

            if (tokenIndex > frame.FirstToken)
            {
                var first = tokens[frame.FirstToken];
                var last = tokens[tokenIndex - 1];
                FuzzAssert.Equal(TextSpan.FromBounds(first.SpanStart, Math.Max(first.SpanStart, last.Span.End)), node.Span, $"The span of the node {node.RawKind} at {fullSpan} does not go from its first token ({first.RawKind} at {first.Span}) to its last one ({last.RawKind} at {last.Span})");
            }
            else
            {
                FuzzAssert.Equal(0, node.Span.Length, $"The node {node.RawKind} at {fullSpan} has no token but its span is {node.Span}");
            }
        }

        FuzzAssert.Equal(tokens.Count, tokenIndex, "Walking the children of the nodes does not reach every token of DescendantTokens");
    }

    private static NodeFrame EnterNode(SyntaxNode node, string text, int firstToken, ref long budget)
    {
        var fullSpan = node.FullSpan;
        FuzzAssert.True(fullSpan.Start >= 0 && fullSpan.End <= text.Length, $"The node {node.RawKind} has the span {fullSpan} outside a text of {text.Length} characters");
        FuzzAssert.True(fullSpan.Contains(node.Span), $"The span {node.Span} of the node {node.RawKind} is outside its full span {fullSpan}");
        if (budget > 0 && node.Parent is not null)
        {
            budget -= fullSpan.Length;
            FuzzAssert.True(text.AsSpan(fullSpan.Start, fullSpan.Length).SequenceEqual(node.ToFullString()), $"The node {node.RawKind} at {fullSpan} does not have the text of its span");
        }

        return new NodeFrame(node, firstToken);
    }

    private static void CheckNavigation(SyntaxNode root, SourceText sourceText, List<SyntaxToken> tokens)
    {
        var text = sourceText.Text;
        FuzzAssert.True(root.GetFirstToken() == tokens[0], "GetFirstToken is not the first token of DescendantTokens");
        FuzzAssert.True(root.GetLastToken() == tokens[^1], "GetLastToken is not the last token of DescendantTokens");

        // Stepping from a token to the next scans the siblings of its ancestors, so it is checked on a sample of tokens
        var step = Math.Max(1, tokens.Count / SampleCount);
        for (var i = 0; i < tokens.Count; i += step)
        {
            var expectedNext = i + 1 < tokens.Count ? tokens[i + 1] : default;
            FuzzAssert.True(tokens[i].GetNextToken() == expectedNext, $"GetNextToken of the token {i} ({tokens[i].RawKind} at {tokens[i].Span}) is not the next token of DescendantTokens");
            var expectedPrevious = i > 0 ? tokens[i - 1] : default;
            FuzzAssert.True(tokens[i].GetPreviousToken() == expectedPrevious, $"GetPreviousToken of the token {i} ({tokens[i].RawKind} at {tokens[i].Span}) is not the previous token of DescendantTokens");
        }

        var positionStep = Math.Max(1, text.Length / SampleCount);
        for (var position = 0; position <= text.Length; position += positionStep)
        {
            CheckFindToken(root, sourceText, tokens, position);
        }

        CheckFindToken(root, sourceText, tokens, text.Length);
    }

    private static void CheckFindToken(SyntaxNode root, SourceText sourceText, List<SyntaxToken> tokens, int position)
    {
        var text = sourceText.Text;
        // The tokens tile the text, so the token that contains a position is the first one that ends after it. Nothing
        // covers the very end of the text, where the end-of-file token sits.
        SyntaxToken expected;
        if (position == text.Length)
        {
            expected = tokens[^1];
        }
        else
        {
            var low = 0;
            var high = tokens.Count - 1;
            while (low < high)
            {
                var middle = low + ((high - low) / 2);
                if (tokens[middle].FullSpan.End > position)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            expected = tokens[low];
        }

        var actual = root.FindToken(position);
        FuzzAssert.True(actual == expected, $"FindToken({position}) returns the token {actual.RawKind} at {actual.FullSpan} instead of the token {expected.RawKind} at {expected.FullSpan}");

        var trivia = root.FindTrivia(position);
        if (trivia != default)
        {
            FuzzAssert.True(trivia.Span.Contains(position), $"FindTrivia({position}) returns the trivia at {trivia.Span}, which does not contain the position");
        }

        var line = sourceText.GetLine(position);
        FuzzAssert.True(line.Start <= position && line.Start <= line.End && line.LineNumber >= 0 && line.LineNumber < sourceText.Lines.Count, $"GetLine({position}) returns the line {line.LineNumber} at [{line.Start}..{line.End})");
    }

    private static void CheckDiagnostics(SyntaxTree tree, string text, string diagnosticPrefix, bool diagnosticsOnNodes, bool diagnosticsInSourceOrder)
    {
        var diagnostics = tree.GetDiagnostics();
        var previousStart = 0;
        foreach (var diagnostic in diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            FuzzAssert.True(span.Start >= 0 && span.Length >= 0 && span.End <= text.Length, $"The diagnostic {diagnostic.Id} has the span {span} outside a text of {text.Length} characters");
            FuzzAssert.True(diagnostic.Id.StartsWith(diagnosticPrefix, StringComparison.Ordinal), $"The diagnostic {diagnostic.Id} does not start with {diagnosticPrefix}");
            FuzzAssert.False(string.IsNullOrEmpty(diagnostic.Message), $"The diagnostic {diagnostic.Id} has no message");
            FuzzAssert.True(ReferenceEquals(diagnostic.Location.SourceText, tree.GetText()), $"The location of the diagnostic {diagnostic.Id} is not bound to the text of the tree");

            var lineSpan = diagnostic.Location.GetLineSpan();
            FuzzAssert.True(lineSpan.Start.Line >= 0 && lineSpan.Start.Character >= 0 && lineSpan.End.Line >= lineSpan.Start.Line, $"The diagnostic {diagnostic.Id} at {span} has the line span {lineSpan}");
            _ = diagnostic.Location.ToString();

            if (diagnosticsInSourceOrder)
            {
                FuzzAssert.True(span.Start >= previousStart, $"The diagnostic {diagnostic.Id} at {span} comes after a diagnostic that starts at {previousStart}");
                previousStart = span.Start;
            }
        }

        if (diagnosticsOnNodes)
        {
            FuzzAssert.Equal(diagnostics.Count > 0, tree.GetRoot().ContainsDiagnostics, "ContainsDiagnostics of the root does not tell whether the tree has diagnostics");
        }
    }

    /// <summary>A node being walked: the position of its next child, and the index of its first token among the tokens of the tree.</summary>
    private sealed class NodeFrame(SyntaxNode node, int firstToken)
    {
        public ChildSyntaxList.Enumerator Children = node.ChildNodesAndTokens().GetEnumerator();

        public SyntaxNode Node { get; } = node;

        public int FirstToken { get; } = firstToken;

        public int NextChildStart { get; set; } = node.FullSpan.Start;

        public int ChildCount { get; set; }
    }
}
