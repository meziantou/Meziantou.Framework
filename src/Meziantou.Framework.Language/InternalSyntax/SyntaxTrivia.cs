namespace Meziantou.Framework.Language.InternalSyntax;

/// <summary>A run of text a parser keeps but does not give meaning to: whitespace, line breaks, comments.</summary>
internal sealed class SyntaxTrivia : GreenNode
{
    internal SyntaxTrivia(int rawKind, string text)
        : base(rawKind, text.Length)
    {
        Text = text;
        SetFlags(NodeFlags.IsNotMissing);
    }

    private SyntaxTrivia(int rawKind, string text, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(rawKind, diagnostics, annotations)
    {
        Text = text;
        FullWidth = text.Length;
        SetFlags(NodeFlags.IsNotMissing);
    }

    public string Text { get; }

    public override bool IsTrivia => true;
    public override int Width => FullWidth;
    public override int GetLeadingTriviaWidth() => 0;
    public override int GetTrailingTriviaWidth() => 0;
    internal override string? TerminalText => Text;
    internal override object? GetValue() => Text;

    internal override GreenNode? GetSlot(int index) => throw new InvalidOperationException("Trivia has no children.");
    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => this;

    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new SyntaxTrivia(RawKind, Text, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new SyntaxTrivia(RawKind, Text, GetDiagnostics(), annotations);

    protected internal override void WriteTo(TextWriter writer, bool leading, bool trailing) => writer.Write(Text);

    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => throw new InvalidOperationException("Trivia is not projected into a node.");

    /// <summary>Marks this trivia as covering text the parser could not otherwise use.</summary>
    internal SyntaxTrivia AsSkippedText()
    {
        SetFlags(NodeFlags.ContainsSkippedText);

        return this;
    }
}
