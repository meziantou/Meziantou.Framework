using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <summary>
/// A node or a token found by the parser, before the tree is built. A token is a range of the text: the characters that no
/// token covers become trivia when the tree is built. A token is a value, so that the thousands of tokens of a document do
/// not each allocate an object.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct BuildElement
{
    private BuildElement(BuildNode? node, SyntaxKind kind, int start, int end, int value)
    {
        Node = node;
        Kind = kind;
        Start = start;
        End = end;
        Value = value;
    }

    /// <summary>Gets the node, or <see langword="null"/> for a token.</summary>
    public BuildNode? Node { get; }

    public SyntaxKind Kind { get; }

    public int Start { get; }

    public int End { get; }

    /// <summary>Gets the columns of a tab that a container took part of, which a code line starts with.</summary>
    public int Value { get; }

    public bool IsToken => Node is null;

    public static BuildElement Token(SyntaxKind kind, int start, int end, int value = 0) => new(node: null, kind, start, end, value);

    public static implicit operator BuildElement(BuildNode node) => new(node, node.Kind, start: 0, end: 0, value: 0);
}
