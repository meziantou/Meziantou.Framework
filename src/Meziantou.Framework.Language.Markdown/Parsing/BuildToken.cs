namespace Meziantou.Framework.Language.Markdown.Parsing;

/// <param name="value">The columns of a tab that a container took part of, which a code line starts with.</param>
internal sealed class BuildToken(SyntaxKind kind, int start, int end, int value) : BuildElement
{
    public SyntaxKind Kind { get; } = kind;

    public int Start { get; } = start;

    public int End { get; } = end;

    public int Value { get; } = value;
}
