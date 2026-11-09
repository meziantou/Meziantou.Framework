namespace Meziantou.Framework.Diagrams.Mermaid;

/// <summary>The exception thrown when a Mermaid text is not valid.</summary>
public sealed class MermaidParseException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MermaidParseException"/> class.</summary>
    public MermaidParseException()
        : this(new MermaidParseError("The Mermaid text is not valid.", 1, 1))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MermaidParseException"/> class.</summary>
    /// <param name="message">The description of the error.</param>
    public MermaidParseException(string message)
        : this(new MermaidParseError(message, 1, 1))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MermaidParseException"/> class.</summary>
    /// <param name="message">The description of the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public MermaidParseException(string message, Exception? innerException)
        : base(message, innerException)
    {
        Error = new MermaidParseError(message, 1, 1);
    }

    /// <summary>Initializes a new instance of the <see cref="MermaidParseException"/> class.</summary>
    /// <param name="error">The error.</param>
    public MermaidParseException(MermaidParseError error)
        : base(error.ToString())
    {
        Error = error;
    }

    /// <summary>Gets the error, with its position.</summary>
    public MermaidParseError Error { get; }
}
