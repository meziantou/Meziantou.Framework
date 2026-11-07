namespace Meziantou.Framework.Language.InternalSyntax;

/// <summary>A terminal of the grammar, together with the trivia that surrounds it.</summary>
/// <remarks>
/// Trivia is held as a single child: nothing, one trivium, or a list. That keeps
/// <see cref="GetLeadingTriviaWidth"/> an O(1) read, which is what makes a node's span O(1) as well.
/// </remarks>
internal class SyntaxToken : GreenNode
{
    internal SyntaxToken(int rawKind, string text, GreenNode? leadingTrivia, GreenNode? trailingTrivia, bool isMissing)
        : base(rawKind)
    {
        Text = text;
        LeadingTrivia = leadingTrivia;
        TrailingTrivia = trailingTrivia;

        AdjustFlagsAndWidth(leadingTrivia);
        AdjustFlagsAndWidth(trailingTrivia);
        FullWidth += text.Length;

        // Trivia is never missing and passes IsNotMissing up, so a missing token has to take the flag back off.
        if (isMissing)
        {
            ClearFlags(NodeFlags.IsNotMissing);
        }
        else
        {
            SetFlags(NodeFlags.IsNotMissing);
        }
    }

    protected SyntaxToken(int rawKind, string text, GreenNode? leadingTrivia, GreenNode? trailingTrivia, bool isMissing, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(rawKind, diagnostics, annotations)
    {
        Text = text;
        LeadingTrivia = leadingTrivia;
        TrailingTrivia = trailingTrivia;

        AdjustFlagsAndWidth(leadingTrivia);
        AdjustFlagsAndWidth(trailingTrivia);
        FullWidth += text.Length;

        // Trivia is never missing and passes IsNotMissing up, so a missing token has to take the flag back off.
        if (isMissing)
        {
            ClearFlags(NodeFlags.IsNotMissing);
        }
        else
        {
            SetFlags(NodeFlags.IsNotMissing);
        }
    }

    /// <summary>Gets the token exactly as it was spelled in the source.</summary>
    public string Text { get; }

    /// <summary>Gets the value the text denotes, which is the text itself unless the language decoded something else.</summary>
    public virtual string ValueText => Text;

    public GreenNode? LeadingTrivia { get; }
    public GreenNode? TrailingTrivia { get; }

    public sealed override bool IsToken => true;
    public sealed override int Width => Text.Length;
    public sealed override int GetLeadingTriviaWidth() => LeadingTrivia?.FullWidth ?? 0;
    public sealed override int GetTrailingTriviaWidth() => TrailingTrivia?.FullWidth ?? 0;
    internal sealed override string? TerminalText => Text;
    internal override object? GetValue() => Text;

    internal sealed override GreenNode? GetSlot(int index) => throw new InvalidOperationException("A token has no children.");
    internal sealed override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => this;

    internal virtual SyntaxToken WithTrivia(GreenNode? leadingTrivia, GreenNode? trailingTrivia)
        => new(RawKind, Text, leadingTrivia, trailingTrivia, IsMissing, GetDiagnostics(), GetAnnotations());

    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics)
        => new SyntaxToken(RawKind, Text, LeadingTrivia, TrailingTrivia, IsMissing, diagnostics, GetAnnotations());

    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations)
        => new SyntaxToken(RawKind, Text, LeadingTrivia, TrailingTrivia, IsMissing, GetDiagnostics(), annotations);

    protected internal sealed override void WriteTo(TextWriter writer, bool leading, bool trailing)
    {
        if (leading)
        {
            LeadingTrivia?.WriteTo(writer, leading: true, trailing: true);
        }

        writer.Write(Text);

        if (trailing)
        {
            TrailingTrivia?.WriteTo(writer, leading: true, trailing: true);
        }
    }

    internal sealed override SyntaxNode CreateRed(SyntaxNode? parent, int position) => throw new InvalidOperationException("A token is not projected into a node.");

    /// <summary>Marks this token as covering text the parser could not otherwise use.</summary>
    internal SyntaxToken AsSkippedText()
    {
        SetFlags(NodeFlags.ContainsSkippedText);

        return this;
    }
}
