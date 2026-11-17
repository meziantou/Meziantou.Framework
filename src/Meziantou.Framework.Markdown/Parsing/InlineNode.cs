namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>An inline being parsed: a token, a range of the subject, or a construct with children.</summary>
/// <remarks>The parser reuses the nodes from one leaf block to the next: they are only needed until its tokens are found.</remarks>
internal sealed class InlineNode
{
    public SyntaxKind Kind { get; private set; }

    public int Start { get; set; }

    public int End { get; set; }

    // A text token that is a run of emphasis delimiters, which Markdig keeps apart from the text around it
    public bool IsDelimiterRun { get; set; }

    // The value of a token: for the first token of generic attributes, 1 when they are for no element, 2 when they are for the
    // block, because they were after text that is whitespace only, and 3 for the attributes of the first line of a block
    public int Value { get; set; }

    public bool IsToken => Kind < SyntaxKind.Document;

    public InlineNode? Parent { get; private set; }

    public InlineNode? Previous { get; private set; }

    public InlineNode? Next { get; private set; }

    public InlineNode? FirstChild { get; private set; }

    public InlineNode? LastChild { get; private set; }

    /// <summary>Changes the kind of the node, which keeps its place in the tree.</summary>
    public void ChangeKind(SyntaxKind kind) => Kind = kind;

    public InlineNode Reset(SyntaxKind kind, int start, int end)
    {
        Kind = kind;
        Start = start;
        End = end;
        IsDelimiterRun = false;
        Value = 0;
        Parent = null;
        Previous = null;
        Next = null;
        FirstChild = null;
        LastChild = null;
        return this;
    }

    public void Append(InlineNode child)
    {
        child.Parent = this;
        child.Previous = LastChild;
        child.Next = null;
        if (LastChild is null)
        {
            FirstChild = child;
        }
        else
        {
            LastChild.Next = child;
        }

        LastChild = child;
    }

    public void Prepend(InlineNode child)
    {
        child.Parent = this;
        child.Previous = null;
        child.Next = FirstChild;
        if (FirstChild is null)
        {
            LastChild = child;
        }
        else
        {
            FirstChild.Previous = child;
        }

        FirstChild = child;
    }

    public void InsertAfter(InlineNode sibling)
    {
        var parent = Parent!;
        sibling.Parent = parent;
        sibling.Previous = this;
        sibling.Next = Next;
        if (Next is null)
        {
            parent.LastChild = sibling;
        }
        else
        {
            Next.Previous = sibling;
        }

        Next = sibling;
    }

    public void Unlink()
    {
        if (Parent is null)
        {
            return;
        }

        if (Previous is null)
        {
            Parent.FirstChild = Next;
        }
        else
        {
            Previous.Next = Next;
        }

        if (Next is null)
        {
            Parent.LastChild = Previous;
        }
        else
        {
            Next.Previous = Previous;
        }

        Parent = null;
        Previous = null;
        Next = null;
    }
}
