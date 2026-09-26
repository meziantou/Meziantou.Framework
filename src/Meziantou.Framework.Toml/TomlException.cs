using System;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml;

/// <summary>
/// Exception thrown when parsing or serializing TOML fails.
/// </summary>
public sealed class TomlException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlException"/> class.
    /// </summary>
    /// <param name="diagnostics">The diagnostics that caused the exception.</param>
    public TomlException(DiagnosticsBag diagnostics) : base(FormatDiagnostics(diagnostics))
    {
        Diagnostics = diagnostics;
        Span = GetFirstSpanOrNull(diagnostics);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlException"/> class.
    /// </summary>
    public TomlException()
    {
        Diagnostics = new DiagnosticsBag();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlException"/> class.
    /// </summary>
    public TomlException(string message) : base(message)
    {
        Diagnostics = new DiagnosticsBag();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlException"/> class.
    /// </summary>
    public TomlException(string message, Exception innerException) : base(message, innerException)
    {
        Diagnostics = new DiagnosticsBag();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlException"/> class with a specific source location.
    /// </summary>
    public TomlException(TomlSourceSpan span, string message) : this(span, message, innerException: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlException"/> class with a specific source location.
    /// </summary>
    public TomlException(TomlSourceSpan span, string message, Exception? innerException) : base(Format(span, message), innerException)
    {
        Span = span;
        Diagnostics = new DiagnosticsBag();
        Diagnostics.Error(ToLegacySpan(span), message);
    }

    /// <summary>
    /// Gets the diagnostics associated with the exception.
    /// </summary>
    public DiagnosticsBag Diagnostics { get; }

    // The value that failed was read completely and its errors are in the diagnostics of the operation, so the reading can
    // continue with the next value to report the errors of the rest of the document
    internal bool IsRecordedValueError { get; private init; }

    internal static TomlException CreateRecordedValueError(DiagnosticsBag operationDiagnostics) => new(operationDiagnostics) { IsRecordedValueError = true };

    // An error of the program rather than of the TOML input, such as a type without metadata: TryDeserialize does not hide it
    internal bool IsConfigurationError { get; private init; }

    internal static TomlException CreateConfigurationError(string message, Exception? innerException = null)
    {
        return innerException is null
            ? new TomlException(message) { IsConfigurationError = true }
            : new TomlException(message, innerException) { IsConfigurationError = true };
    }

    /// <summary>
    /// Gets the optional source span associated with this exception.
    /// </summary>
    public TomlSourceSpan? Span { get; }

    /// <summary>
    /// Gets the optional source name associated with this exception (for example, a file path).
    /// </summary>
    public string? SourceName => Span?.SourceName;

    /// <summary>
    /// Gets the 1-based line number associated with this exception, or <see langword="null"/> when unknown.
    /// </summary>
    public int? Line => Span?.Start.Line + 1;

    /// <summary>
    /// Gets the 1-based column number associated with this exception, or <see langword="null"/> when unknown.
    /// </summary>
    public int? Column => Span?.Start.Column + 1;

    /// <summary>
    /// Gets the 0-based offset associated with this exception, or <see langword="null"/> when unknown.
    /// </summary>
    /// <remarks>
    /// This offset represents a character offset in the decoded TOML payload. When parsing UTF-8 inputs, the offset is computed after decoding.
    /// </remarks>
    public int? Offset => Span?.Offset;

    private static string Format(TomlSourceSpan span, string message)
    {
        return $"{span.ToStringSimple()} : error : {message}";
    }

    private static TomlSourceSpan? GetFirstSpanOrNull(DiagnosticsBag diagnostics)
    {
        if (diagnostics is null || diagnostics.Count == 0)
        {
            return null;
        }

        var span = diagnostics[0].Span;
        return new TomlSourceSpan(span.FileName, new TomlTextPosition(span.Start.Offset, span.Start.Line, span.Start.Column), new TomlTextPosition(span.End.Offset, span.End.Line, span.End.Column));
    }

    private static SourceSpan ToLegacySpan(TomlSourceSpan span)
        => new SourceSpan(span.SourceName, new TextPosition(span.Start.Offset, span.Start.Line, span.Start.Column), new TextPosition(span.End.Offset, span.End.Line, span.End.Column));

    // The message lists the first diagnostics only: a document with an error on every line would build a message as long as
    // the document, which callers typically log. Diagnostics holds all of them.
    private const int MaxDiagnosticsInMessage = 100;

    private static string FormatDiagnostics(DiagnosticsBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (diagnostics.Count <= MaxDiagnosticsInMessage)
        {
            return diagnostics.ToString();
        }

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < MaxDiagnosticsInMessage; i++)
        {
            builder.AppendLine(diagnostics[i].ToString());
        }

        builder.Append("... and ").Append(diagnostics.Count - MaxDiagnosticsInMessage).AppendLine(" more diagnostics.");
        return builder.ToString();
    }
}
