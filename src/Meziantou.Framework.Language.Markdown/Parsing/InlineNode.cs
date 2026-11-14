namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <summary>An inline being parsed: a token, a range of the subject, or a construct with children.</summary>
/// <remarks>The parser reuses the nodes from one leaf block to the next: they are only needed until its tokens are found.</remarks>
internal sealed class InlineNode
{
    public SyntaxKind Kind { get; private set; }

    public int Start { get; set; }

    public int End { get; set; }

    public bool IsToken => Kind < SyntaxKind.Document;

    public InlineNode? Parent { get; private set; }

    public InlineNode? Previous { get; private set; }

    public InlineNode? Next { get; private set; }

    public InlineNode? FirstChild { get; private set; }

    public InlineNode? LastChild { get; private set; }

    public InlineNode Reset(SyntaxKind kind, int start, int end)
    {
        Kind = kind;
        Start = start;
        End = end;
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
