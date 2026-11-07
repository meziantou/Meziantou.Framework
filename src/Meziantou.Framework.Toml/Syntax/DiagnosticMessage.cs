using System;
using System.Text;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// A diagnostic message with errors.
/// </summary>
public class DiagnosticMessage
{
    /// <summary>
    /// Creates a new instance of a <see cref="DiagnosticMessage"/>
    /// </summary>
    /// <param name="kind">The kind of message</param>
    /// <param name="span">The source span</param>
    /// <param name="message">The message</param>
    public DiagnosticMessage(DiagnosticMessageKind kind, SourceSpan span, string message)
    {
        Kind = kind;
        Span = span;
        Message = message;
    }

    /// <summary>
    /// Gets the kind of message.
    /// </summary>
    public DiagnosticMessageKind Kind { get; }

    /// <summary>
    /// Gets the source span.
    /// </summary>
    public SourceSpan Span { get; }

    /// <summary>
    /// Gets the message.
    /// </summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(Span.ToStringSimple());
        builder.Append(" : ");
        switch (Kind)
        {
            case DiagnosticMessageKind.Error:
                builder.Append("error");
                break;
            case DiagnosticMessageKind.Warning:
                builder.Append("warning");
                break;
            default:
                builder.Append(Kind.ToString());
                break;
        }
        builder.Append(" : ");
        if (Message != null)
        {
            builder.Append(Message);
        }
        return builder.ToString();
    }
}
