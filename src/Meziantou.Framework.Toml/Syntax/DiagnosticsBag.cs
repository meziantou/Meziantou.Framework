using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// A container for <see cref="DiagnosticMessage"/>
/// </summary>
[DebuggerDisplay("{Count} Errors: {HasErrors}")]
public class DiagnosticsBag : IEnumerable<DiagnosticMessage>
{
    private readonly List<DiagnosticMessage> _messages;

    /// <summary>
    /// Creates a new instance of a <see cref="DiagnosticsBag"/>
    /// </summary>
    public DiagnosticsBag()
    {
        _messages = new List<DiagnosticMessage>();
    }

    /// <summary>
    /// Creates a new instance of a <see cref="DiagnosticsBag"/>.
    /// </summary>
    /// <param name="messages">An existing list of messages.</param>
    public DiagnosticsBag(IEnumerable<DiagnosticMessage> messages) : this()
    {
        foreach (var message in messages)
        {
            Add(message);
        }
    }

    /// <summary>
    /// Gets the number of messages.
    /// </summary>
    public int Count => _messages.Count;

    /// <summary>
    /// Gets the message at the specified index.
    /// </summary>
    /// <param name="index">Index of the message.</param>
    /// <returns>A diagnostic message.</returns>
    public DiagnosticMessage this[int index] => _messages[index];

    /// <summary>
    /// Gets a boolean indicating if this bag contains any error messages.
    /// </summary>
    public bool HasErrors { get; private set; }

    /// <summary>
    /// Adds the specified message to this bag.
    /// </summary>
    /// <param name="message">The message to add</param>
    public void Add(DiagnosticMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Add(message);
        if (message.Kind == DiagnosticMessageKind.Error)
        {
            HasErrors = true;
        }
    }

    /// <summary>
    /// Adds the specified list of messages to this bag.
    /// </summary>
    /// <param name="messages">A list of messages.</param>
    public void AddRange(IEnumerable<DiagnosticMessage> messages)
    {
        foreach (var diagnosticMessage in messages)
        {
            Add(diagnosticMessage);
        }
    }

    /// <summary>
    /// Clear this bag including the error state.
    /// </summary>
    public void Clear()
    {
        _messages.Clear();
        HasErrors = false;
    }

    /// <summary>
    /// Adds a warning message
    /// </summary>
    /// <param name="span">The source span</param>
    /// <param name="text">The warning message</param>
    public void Warning(SourceSpan span, string text)
    {
        Add(new DiagnosticMessage(DiagnosticMessageKind.Warning, span, text));
    }

    /// <summary>
    /// Adds an error message
    /// </summary>
    /// <param name="span">The source span</param>
    /// <param name="text">The error message</param>
    public void Error(SourceSpan span, string text)
    {
        Add(new DiagnosticMessage(DiagnosticMessageKind.Error, span, text));
    }

    /// <summary>
    /// Gets the enumerator of <see cref="DiagnosticMessage"/>
    /// </summary>
    /// <returns></returns>
    public List<DiagnosticMessage>.Enumerator GetEnumerator()
    {
        return _messages.GetEnumerator();
    }

    IEnumerator<DiagnosticMessage> IEnumerable<DiagnosticMessage>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable) _messages).GetEnumerator();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var message in _messages)
        {
            builder.AppendLine(message.ToString());
        }

        return builder.ToString();
    }
}
