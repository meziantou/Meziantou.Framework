namespace Meziantou.Framework.Language.InternalSyntax;

/// <summary>A diagnostic attached to a green node, positioned relative to that node.</summary>
/// <remarks>
/// The offset is relative so the green node stays position-independent: the same node can be reused at a different
/// place in a later tree, and its diagnostics move with it. Absolute positions are computed only when the diagnostic
/// is materialized against a tree.
/// </remarks>
internal sealed class SyntaxDiagnosticInfo
{
    private readonly object?[]? _arguments;

    public SyntaxDiagnosticInfo(int offset, int width, DiagnosticDescriptor descriptor, params object?[]? arguments)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentNullException.ThrowIfNull(descriptor);

        Offset = offset;
        Width = width;
        Descriptor = descriptor;
        _arguments = arguments;
    }

    /// <summary>Gets the start of the diagnostic, relative to the full start of the node that owns it.</summary>
    public int Offset { get; }

    public int Width { get; }
    public DiagnosticDescriptor Descriptor { get; }
    public string Id => Descriptor.Id;
    public DiagnosticSeverity Severity => Descriptor.DefaultSeverity;

    public string GetMessage() => Diagnostic.FormatMessage(Descriptor.MessageFormat, _arguments);

    /// <summary>Returns the same diagnostic positioned at a different offset within its owner.</summary>
    public SyntaxDiagnosticInfo WithOffset(int offset) => new(offset, Width, Descriptor, _arguments);

    /// <summary>Materializes the diagnostic against the absolute position its owning node sits at.</summary>
    public Diagnostic ToDiagnostic(int nodePosition, SourceText? sourceText)
    {
        var span = new TextSpan(nodePosition + Offset, Width);

        return new Diagnostic(Id, GetMessage(), Severity, new Location(span, sourceText));
    }

    public override string ToString() => $"{Id} @{Offset}+{Width}: {GetMessage()}";
}
