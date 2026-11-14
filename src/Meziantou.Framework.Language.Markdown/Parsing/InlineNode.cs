namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <summary>An inline being parsed: a token, a range of the subject, or a construct with children.</summary>
internal sealed class InlineNode(SyntaxKind kind, int start, int end)
{
    public SyntaxKind Kind { get; } = kind;

    public int Start { get; set; } = start;

    public int End { get; set; } = end;

    public bool IsToken => Kind < SyntaxKind.Document;

    public InlineNode? Parent { get; private set; }

    public InlineNode? Previous { get; private set; }

    public InlineNode? Next { get; private set; }

    public InlineNode? FirstChild { get; private set; }

    public InlineNode? LastChild { get; private set; }

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

    public IEnumerable<InlineNode> Children()
    {
        for (var child = FirstChild; child is not null; child = child.Next)
        {
            yield return child;
        }
    }
}
