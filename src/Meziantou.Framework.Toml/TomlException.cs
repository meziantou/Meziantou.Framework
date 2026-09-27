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
        _diagnostics = diagnostics;
        Span = GetFirstSpanOrNull(diagnostics);
    }

    // A recorded value error is thrown for every table with an error, and caught by its parent: formatting its message
    // eagerly would copy the first diagnostics once per table. It describes the errors of its value only, which do not change
    // when the operation records more errors.
    private TomlException(DiagnosticsBag operationDiagnostics, int firstDiagnostic, TomlSourceSpan? recordedValueStart)
    {
        OperationDiagnostics = operationDiagnostics;
        _firstRecordedDiagnostic = firstDiagnostic;
        _recordedDiagnosticsEnd = operationDiagnostics.Count;
        Span = firstDiagnostic < operationDiagnostics.Count ? ToSpan(operationDiagnostics[firstDiagnostic].Span) : null;
        IsRecordedValueError = true;
        RecordedValueStart = recordedValueStart;
    }

    private readonly int _firstRecordedDiagnostic;
    private readonly int _recordedDiagnosticsEnd;
    private DiagnosticsBag? _diagnostics;
    private string? _lazyMessage;

    /// <inheritdoc />
    public override string Message => IsRecordedValueError ? _lazyMessage ??= FormatDiagnostics(Diagnostics) : base.Message;

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlException"/> class.
    /// </summary>
    public TomlException()
    {
        _diagnostics = new DiagnosticsBag();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlException"/> class.
    /// </summary>
    public TomlException(string message) : base(message)
    {
        _diagnostics = new DiagnosticsBag();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlException"/> class.
    /// </summary>
    public TomlException(string message, Exception innerException) : base(message, innerException)
    {
        _diagnostics = new DiagnosticsBag();
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
        _diagnostics = new DiagnosticsBag();
        _diagnostics.Error(ToLegacySpan(span), message);
    }

    /// <summary>
    /// Gets the diagnostics associated with the exception.
    /// </summary>
    public DiagnosticsBag Diagnostics => _diagnostics ??= CreateRecordedDiagnostics();

    // The diagnostics of the operation of a recorded value error, which tell whether it belongs to an operation
    internal DiagnosticsBag? OperationDiagnostics { get; }

    // The value that failed was read completely and its errors are in the diagnostics of the operation, so the reading can
    // continue with the next value to report the errors of the rest of the document
    internal bool IsRecordedValueError { get; }

    // The start of the value that was read: only the reader of that value can continue with the next one. The error can go
    // through frames that have not finished their own value, such as a converter reading a nested value.
    internal TomlSourceSpan? RecordedValueStart { get; }

    internal static TomlException CreateRecordedValueError(DiagnosticsBag operationDiagnostics, int firstDiagnostic, TomlSourceSpan? valueStart) => new(operationDiagnostics, firstDiagnostic, valueStart);

    private DiagnosticsBag CreateRecordedDiagnostics()
    {
        var diagnostics = new DiagnosticsBag();
        for (var i = _firstRecordedDiagnostic; i < _recordedDiagnosticsEnd; i++)
        {
            diagnostics.Add(OperationDiagnostics![i]);
        }

        return diagnostics;
    }

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
        return Truncate($"{span.ToStringSimple()} : error : {message}", MaxMessageLength);
    }

    private static TomlSourceSpan? GetFirstSpanOrNull(DiagnosticsBag diagnostics)
    {
        if (diagnostics is null || diagnostics.Count == 0)
        {
            return null;
        }

        return ToSpan(diagnostics[0].Span);
    }

    private static TomlSourceSpan ToSpan(SourceSpan span)
        => new(span.FileName, new TomlTextPosition(span.Start.Offset, span.Start.Line, span.Start.Column), new TomlTextPosition(span.End.Offset, span.End.Line, span.End.Column));

    private static SourceSpan ToLegacySpan(TomlSourceSpan span)
        => new SourceSpan(span.SourceName, new TextPosition(span.Start.Offset, span.Start.Line, span.Start.Column), new TextPosition(span.End.Offset, span.End.Line, span.End.Column));

    // The message lists the first diagnostics only: a document with an error on every line would build a message as long as
    // the document, which callers typically log. Diagnostics holds all of them.
    private const int MaxDiagnosticsInMessage = 100;
    private const int MaxMessageLength = 100_000;

    private static string FormatDiagnostics(DiagnosticsBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < diagnostics.Count; i++)
        {
            // Some messages contain input text, so their number does not bound the length
            if (i == MaxDiagnosticsInMessage || builder.Length >= MaxMessageLength)
            {
                builder.Append("... and ").Append(diagnostics.Count - i).AppendLine(" more diagnostics.");
                break;
            }

            // A single diagnostic can embed long input text too
            builder.AppendLine(Truncate(diagnostics[i].ToString(), MaxMessageLength - builder.Length));
        }

        return builder.ToString();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return string.Concat(text.AsSpan(0, length), "...");
    }
}
