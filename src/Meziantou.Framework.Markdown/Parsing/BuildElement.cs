using System.Runtime.InteropServices;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// A node or a token found by the parser, before the tree is built. The elements of a tree are in a flat list, in the order of
/// the text: a node is followed by its descendants, so that building a tree allocates no list per node. A token is a range of the
/// text: the characters that no token covers become trivia when the tree is built.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct BuildElement
{
    private BuildElement(SyntaxKind kind, int start, int end, int value)
    {
        Kind = kind;
        Start = start;
        End = end;
        Value = value;
    }

    public SyntaxKind Kind { get; }

    public int Start { get; }

    public int End { get; }

    /// <summary>
    /// Gets the value of a token, such as the columns of a tab that a container took part of, which a code line starts with, or the
    /// number of descendants of a node.
    /// </summary>
    public int Value { get; }

    public bool IsToken => Kind < SyntaxKind.Document;

    public static BuildElement Token(SyntaxKind kind, int start, int end, int value = 0) => new(kind, start, end, value);

    public static BuildElement Node(SyntaxKind kind, int descendantCount) => new(kind, start: 0, end: 0, descendantCount);
}
