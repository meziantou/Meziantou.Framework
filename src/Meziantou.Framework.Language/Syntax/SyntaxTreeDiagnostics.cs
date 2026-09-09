using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Syntax;

/// <summary>Turns the diagnostics attached to immutable nodes into diagnostics with absolute positions.</summary>
/// <remarks>
/// Each diagnostic is stored relative to the node that owns it, so this walk carries a running position and only
/// resolves the absolute one as it goes. A subtree carrying no diagnostic is skipped whole, which makes a clean tree
/// cost one flag test.
/// </remarks>
internal static class SyntaxTreeDiagnostics
{
    /// <summary>Returns every diagnostic at or below <paramref name="node"/>, in source order.</summary>
    /// <remarks>
    /// The walk cannot produce them in order on its own: a node's own diagnostic is found before its children are
    /// visited, yet its offset may fall after theirs. They are ordered by position afterwards, which is stable, so
    /// two diagnostics at the same position keep the order the walk found them in.
    /// </remarks>
    public static IEnumerable<Diagnostic> Enumerate(GreenNode? node, int position, SourceText? sourceText)
        => Walk(node, position, sourceText).OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start);

    private static IEnumerable<Diagnostic> Walk(GreenNode? node, int position, SourceText? sourceText)
    {
        if (node is null || !node.ContainsDiagnostics)
            yield break;

        var stack = new Stack<(GreenNode Node, int Position)>();
        stack.Push((node, position));

        while (stack.TryPop(out var entry))
        {
            var (current, currentPosition) = entry;
            if (!current.ContainsDiagnostics)
                continue;

            if (current is GreenToken token)
            {
                foreach (var diagnostic in TokenDiagnostics(token, currentPosition, sourceText))
                {
                    yield return diagnostic;
                }

                continue;
            }

            foreach (var info in current.GetDiagnostics())
            {
                yield return info.ToDiagnostic(currentPosition, sourceText);
            }

            // Pushed in reverse so children come out in source order.
            for (var i = current.SlotCount - 1; i >= 0; i--)
            {
                if (current.GetSlot(i) is { } child)
                {
                    stack.Push((child, currentPosition + current.GetSlotOffset(i)));
                }
            }
        }
    }

    private static IEnumerable<Diagnostic> TokenDiagnostics(GreenToken token, int position, SourceText? sourceText)
    {
        foreach (var diagnostic in TriviaDiagnostics(token.LeadingTrivia, position, sourceText))
        {
            yield return diagnostic;
        }

        foreach (var info in token.GetDiagnostics())
        {
            yield return info.ToDiagnostic(position, sourceText);
        }

        var trailingStart = position + token.GetLeadingTriviaWidth() + token.Text.Length;
        foreach (var diagnostic in TriviaDiagnostics(token.TrailingTrivia, trailingStart, sourceText))
        {
            yield return diagnostic;
        }
    }

    private static IEnumerable<Diagnostic> TriviaDiagnostics(GreenNode? trivia, int position, SourceText? sourceText)
    {
        if (trivia is null || !trivia.ContainsDiagnostics)
            yield break;

        var count = GreenNodeList.Count(trivia);
        for (var i = 0; i < count; i++)
        {
            var item = GreenNodeList.ElementAt(trivia, i)!;
            if (!item.ContainsDiagnostics)
                continue;

            foreach (var info in item.GetDiagnostics())
            {
                yield return info.ToDiagnostic(position + GreenNodeList.OffsetAt(trivia, i), sourceText);
            }
        }
    }
}
