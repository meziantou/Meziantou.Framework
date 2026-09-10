using System.Diagnostics;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>Offers the parser pieces of the previous tree that the edit did not touch.</summary>
/// <remarks>
/// <para>
/// The rule that keeps this honest is that the parser is always driven by the lexer. A position only moves because a
/// token was really read out of the new text, or because a node was adopted whose text is known to match. Nothing is
/// worked out by arithmetic alone.
/// </para>
/// <para>
/// That matters more than it sounds. Type a single quote in the middle of a document and the lexer reads one
/// unterminated string running to the end of the file; it never again stops where an old node began, so nothing after
/// the quote is offered. A blender that trusted its own sums would happily reuse all of it and produce a tree that
/// does not match its own text.
/// </para>
/// </remarks>
internal sealed class Blender
{
    private readonly string _oldTextValue;
    private readonly string _newTextValue;
    private readonly TextChangeRange _change;
    private readonly TextSpan _untouchable;
    private readonly Stack<ChildSyntaxList.Enumerator> _path = new();
    private SyntaxNodeOrToken _current;
    private bool _exhausted;

    public Blender(SourceText oldText, SourceText newText, TextChangeRange change, SyntaxNode oldRoot)
    {
        _oldTextValue = oldText.Text;
        _newTextValue = newText.Text;
        _change = change;
        _untouchable = change.Span;

        _path.Push(oldRoot.ChildNodesAndTokens().GetEnumerator());
        MoveToNextInPath();
    }

    /// <summary>Gets how many nodes were taken from the previous tree rather than parsed again.</summary>
    public int ReusedNodeCount { get; private set; }

    /// <summary>Where in the grammar a node is being asked for. A member cannot stand in for a value.</summary>
    public enum NodeContext
    {
        Value,
        Member,
    }

    /// <summary>Returns the node from the previous tree that belongs at <paramref name="newPosition"/>, if any.</summary>
    public GreenNode? TryTakeNode(int newPosition, NodeContext context)
    {
        var oldPosition = MapToOld(newPosition);
        if (oldPosition < 0)
            return null;

        // The parser only ever moves forward, so the cursor does too.
        while (!_exhausted && _current.FullSpan.Start < oldPosition)
        {
            if (_current.FullSpan.End <= oldPosition)
            {
                MoveToNextInPath();
            }
            else
            {
                Advance();
            }
        }

        // A node and its first child start at the same place, so the widest one is offered first and the search
        // descends only when it is turned down.
        while (!_exhausted && _current.FullSpan.Start == oldPosition)
        {
            if (_current.AsNode(out var node) && CanReuse(node, oldPosition, context))
            {
                Debug.Assert(
                    string.CompareOrdinal(_newTextValue, newPosition, _oldTextValue, oldPosition, node.Green.FullWidth) == 0,
                    "A node was about to be reused where the text does not match.");

                MoveToNextInPath();
                ReusedNodeCount++;

                return node.Green;
            }

            Advance();
        }

        return null;
    }

    /// <summary>Maps a place in the new text back to the old one, or -1 when it falls inside what changed.</summary>
    private int MapToOld(int newPosition)
    {
        if (newPosition < _change.Span.Start)
            return newPosition;

        var newChangeEnd = _change.Span.Start + _change.NewLength;

        return newPosition >= newChangeEnd ? newPosition - _change.Delta : -1;
    }

    private bool CanReuse(SyntaxNode node, int oldStart, NodeContext context)
    {
        var green = node.Green;
        if (green.FullWidth == 0 || green.IsMissing)
            return false;

        // A node carrying any of these was shaped by its surroundings, and those may not survive the edit.
        if (green.ContainsDiagnostics || green.ContainsSkippedText || green.ContainsAnnotations)
            return false;

        if (!IsAllowedInContext((SyntaxKind)green.RawKind, context))
            return false;

        // A character either side, because an insertion touching an edge can join two lexemes into one: 12 and 3
        // become 123, / and / become a comment.
        var probe = TextSpan.FromBounds(Math.Max(0, oldStart - 1), oldStart + green.FullWidth + 1);

        return !probe.IntersectsWith(_untouchable);
    }

    private static bool IsAllowedInContext(SyntaxKind kind, NodeContext context) => context switch
    {
        NodeContext.Member => kind == SyntaxKind.JsonMember,
        NodeContext.Value => kind is SyntaxKind.JsonObject or SyntaxKind.JsonArray or SyntaxKind.JsonString
            or SyntaxKind.JsonNumber or SyntaxKind.JsonTrueLiteral or SyntaxKind.JsonFalseLiteral or SyntaxKind.JsonNullLiteral,
        _ => false,
    };

    /// <summary>Steps to the next item, looking inside the current one first.</summary>
    private void Advance()
    {
        if (_current.AsNode(out var node) && node.ChildNodesAndTokens().Count > 0)
        {
            _path.Push(node.ChildNodesAndTokens().GetEnumerator());
        }

        MoveToNextInPath();
    }

    /// <summary>Steps to the next item without looking inside the current one.</summary>
    private void MoveToNextInPath()
    {
        while (_path.Count > 0)
        {
            var enumerator = _path.Pop();
            if (enumerator.MoveNext())
            {
                _current = enumerator.Current;
                _path.Push(enumerator);

                return;
            }
        }

        _exhausted = true;
    }
}
