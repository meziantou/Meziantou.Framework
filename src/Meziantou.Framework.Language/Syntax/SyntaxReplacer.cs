using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Syntax;

/// <summary>Rebuilds a tree with some of its parts replaced.</summary>
/// <remarks>
/// <para>
/// Only the spine from a replaced part up to the root is rebuilt. Every subtree that contains nothing to replace is
/// carried over by reference, so an edit costs the depth of the tree rather than its size, and the parts that did not
/// change stay the very same immutable nodes -- which is what makes it possible to ask, afterwards, what actually
/// changed.
/// </para>
/// <para>
/// Nodes are matched by identity, and tokens and trivia by the immutable node behind them together with their
/// position, because a token is a view rather than an object and two views of the same token compare equal only when
/// they agree on both.
/// </para>
/// </remarks>
internal sealed class SyntaxReplacer
{
    private readonly Dictionary<SyntaxNode, GreenNode?> _nodes = [];
    private readonly Dictionary<(GreenNode Green, int Position), GreenNode?> _tokens = [];
    private readonly Dictionary<(GreenNode Green, int Position), GreenNode?> _trivia = [];
    private readonly Dictionary<SyntaxNode, Func<GreenNode?[], GreenNode?[]>> _slotEdits = [];
    private TextSpan? _bounds;
    private bool _replacedAnything;

    public void ReplaceNode(SyntaxNode oldNode, GreenNode? newGreen)
    {
        _nodes[oldNode] = newGreen;
        Extend(oldNode.FullSpan);
    }

    public void ReplaceToken(SyntaxToken oldToken, GreenNode? newGreen)
    {
        if (oldToken.Node is not { } green)
            return;

        _tokens[(green, oldToken.FullSpan.Start)] = newGreen;
        Extend(oldToken.FullSpan);
    }

    public void ReplaceTrivia(SyntaxTrivia oldTrivia, GreenNode? newGreen)
    {
        if (oldTrivia.UnderlyingNode is not { } green)
            return;

        _trivia[(green, oldTrivia.FullSpan.Start)] = newGreen;
        Extend(oldTrivia.FullSpan);
    }

    /// <summary>
    /// Registers a change to the slots of <paramref name="node"/>, applied once everything below it has been rebuilt.
    /// </summary>
    /// <remarks>
    /// Running after the children means a change to a node and a change to something inside it both take effect: the
    /// walk still goes all the way down, and each change is applied on the way back out.
    /// </remarks>
    public void EditSlots(SyntaxNode node, Func<GreenNode?[], GreenNode?[]> edit)
    {
        // Composed rather than replaced: a node can have more than one of its slots edited in the same pass, and
        // keeping only the last registration would drop the others without saying so.
        _slotEdits[node] = _slotEdits.TryGetValue(node, out var registered) ? slots => edit(registered(slots)) : edit;
        Extend(node.FullSpan);
    }

    /// <summary>Rebuilds <paramref name="root"/> and reports whether anything was actually replaced.</summary>
    public GreenNode Rebuild(SyntaxNode root, out bool replacedAnything)
    {
        _replacedAnything = false;
        var result = RebuildNode(root) ?? throw new InvalidOperationException("The root of a tree cannot be removed.");
        replacedAnything = _replacedAnything;

        return result;
    }

    private void Extend(TextSpan span) => _bounds = _bounds is { } bounds ? TextSpan.FromBounds(Math.Min(bounds.Start, span.Start), Math.Max(bounds.End, span.End)) : span;

    private bool CouldContainATarget(TextSpan span) => _bounds is not { } bounds || span.IntersectsWith(bounds);

    /// <summary>Rebuilds <paramref name="root"/>, replacing whatever was registered below it.</summary>
    /// <remarks>
    /// The descent keeps its own stack rather than recursing, so a deeply nested document does not become a deep call
    /// stack. The pruning above keeps the walk to the spine down to what is being replaced, but that spine is as deep
    /// as the target, and the parsers here are iterative: they read documents nested far deeper than a recursive walk
    /// could rebuild.
    /// </remarks>
    private GreenNode? RebuildNode(SyntaxNode root)
    {
        var frame = StartFrame(root, out var resolved);
        if (frame is null)
            return resolved;

        var stack = new Stack<Frame>();
        stack.Push(frame);

        // The result of the frame that finished last, waiting to be written into the slot its parent stopped at. It
        // can legitimately be null -- that is a removal -- so a separate flag says whether one is pending.
        GreenNode? completed = null;
        var hasCompleted = false;

        while (stack.Count > 0)
        {
            var current = stack.Peek();
            if (hasCompleted)
            {
                current.Accept(completed);
                hasCompleted = false;
            }

            var descended = false;
            while (current.Slot < current.Green.SlotCount)
            {
                var index = current.Slot;
                var childGreen = current.Green.GetSlot(index);
                if (childGreen is null)
                {
                    current.Slot++;
                    continue;
                }

                if (childGreen.IsToken)
                {
                    current.Accept(RebuildToken(current.Node, index, childGreen));
                    continue;
                }

                if (current.Node.GetNodeSlot(index) is not { } childRed)
                {
                    current.Accept(RebuildTokenList(current.Node, index, childGreen));
                    continue;
                }

                if (StartFrame(childRed, out var childResolved) is not { } childFrame)
                {
                    current.Accept(childResolved);
                    continue;
                }

                // The slot stays where it is: the child's result arrives through Accept once its frame is done.
                stack.Push(childFrame);
                descended = true;
                break;
            }

            if (descended)
                continue;

            stack.Pop();
            completed = Finish(current);
            hasCompleted = true;
        }

        return completed;
    }

    /// <summary>
    /// Begins rebuilding <paramref name="node"/>, or answers outright with <paramref name="resolved"/> when there is
    /// nothing under it to walk.
    /// </summary>
    private Frame? StartFrame(SyntaxNode node, out GreenNode? resolved)
    {
        if (_nodes.TryGetValue(node, out var replacement))
        {
            _replacedAnything = true;
            resolved = replacement;

            return null;
        }

        var hasSlotEdit = _slotEdits.TryGetValue(node, out var edit);
        if (!hasSlotEdit && !CouldContainATarget(node.FullSpan))
        {
            resolved = node.Green;

            return null;
        }

        resolved = null;

        return new Frame(node, hasSlotEdit ? edit : null);
    }

    private GreenNode? Finish(Frame frame)
    {
        var newSlots = frame.NewSlots;
        if (frame.Edit is { } edit)
        {
            newSlots = edit(newSlots ?? CopySlots(frame.Green));
            _replacedAnything = true;
        }

        return newSlots is null ? frame.Green : frame.Green.WithSlots(newSlots);
    }

    /// <summary>
    /// Rebuilds a slot holding a list of tokens rather than of nodes.
    /// </summary>
    /// <remarks>
    /// Such a list has no red node of its own, so the tokens inside it are only reachable from here. Without this a
    /// token in a list -- the tokens of a skipped-text node, say -- could never be replaced.
    /// </remarks>
    private GreenNode? RebuildTokenList(SyntaxNode parent, int slot, GreenNode green)
    {
        var position = parent.GetChildPosition(slot);
        if (!CouldContainATarget(new TextSpan(position, green.FullWidth)))
            return green;

        GreenNode?[]? newSlots = null;
        for (var i = 0; i < green.SlotCount; i++)
        {
            var childGreen = green.GetSlot(i);
            if (childGreen is null)
                continue;

            var newChild = RebuildTokenAt(childGreen, position);
            position += childGreen.FullWidth;
            if (ReferenceEquals(newChild, childGreen))
                continue;

            newSlots ??= CopySlots(green);
            newSlots[i] = newChild;
        }

        return newSlots is null ? green : green.WithSlots(newSlots);
    }

    private GreenNode? RebuildToken(SyntaxNode parent, int slot, GreenNode green) => RebuildTokenAt(green, parent.GetChildPosition(slot));

    private GreenNode? RebuildTokenAt(GreenNode green, int position)
    {
        if (_tokens.TryGetValue((green, position), out var replacement))
        {
            _replacedAnything = true;

            return replacement;
        }

        if (_trivia.Count == 0 || green is not GreenToken token || !CouldContainATarget(new TextSpan(position, green.FullWidth)))
            return green;

        var leading = RebuildTrivia(token.LeadingTrivia, position);
        var trailing = RebuildTrivia(token.TrailingTrivia, position + token.GetLeadingTriviaWidth() + token.Text.Length);
        if (ReferenceEquals(leading, token.LeadingTrivia) && ReferenceEquals(trailing, token.TrailingTrivia))
            return green;

        return token.WithTrivia(leading, trailing);
    }

    private GreenNode? RebuildTrivia(GreenNode? trivia, int position)
    {
        if (trivia is null)
            return null;

        var count = GreenNodeList.Count(trivia);
        GreenNode?[]? replaced = null;
        for (var i = 0; i < count; i++)
        {
            var item = GreenNodeList.ElementAt(trivia, i)!;
            if (!_trivia.TryGetValue((item, position + GreenNodeList.OffsetAt(trivia, i)), out var replacement))
                continue;

            replaced ??= GreenNodeList.ToArray(trivia);
            replaced[i] = replacement;
            _replacedAnything = true;
        }

        return replaced is null ? trivia : InternalSyntax.SyntaxList.List(replaced);
    }

    private static GreenNode?[] CopySlots(GreenNode green)
    {
        var slots = new GreenNode?[green.SlotCount];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = green.GetSlot(i);
        }

        return slots;
    }

    /// <summary>One node part-way through being rebuilt: which slot the walk is at, and what it has produced so far.</summary>
    private sealed class Frame(SyntaxNode node, Func<GreenNode?[], GreenNode?[]>? edit)
    {
        public SyntaxNode Node { get; } = node;

        public GreenNode Green { get; } = node.Green;

        public Func<GreenNode?[], GreenNode?[]>? Edit { get; } = edit;

        /// <summary>The slot the walk is at, which is the one a result coming back belongs in.</summary>
        public int Slot { get; set; }

        /// <summary>The slots as rebuilt, left null while every one of them is still the original.</summary>
        public GreenNode?[]? NewSlots { get; private set; }

        /// <summary>Records what the current slot rebuilt to and moves on to the next.</summary>
        public void Accept(GreenNode? value)
        {
            if (!ReferenceEquals(value, Green.GetSlot(Slot)))
            {
                NewSlots ??= CopySlots(Green);
                NewSlots[Slot] = value;
            }

            Slot++;
        }
    }
}
