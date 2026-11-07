namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The exception thrown when a source text is not a valid script or module (an early error or a syntax error).</summary>
public sealed class JsSyntaxErrorException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="JsSyntaxErrorException"/> class.</summary>
    public JsSyntaxErrorException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsSyntaxErrorException"/> class with a message.</summary>
    public JsSyntaxErrorException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsSyntaxErrorException"/> class with a message and an inner exception.</summary>
    public JsSyntaxErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsSyntaxErrorException"/> class with a message and a location.</summary>
    public JsSyntaxErrorException(string message, string? url, int offset, int line, int column)
        : base(message)
    {
        Url = url;
        Offset = offset;
        Line = line;
        Column = column;
    }

    /// <summary>Gets the URL of the source, if any.</summary>
    public string? Url { get; }

    /// <summary>Gets the offset of the error in the source text.</summary>
    public int Offset { get; }

    /// <summary>Gets the 1-based line of the error.</summary>
    public int Line { get; }

    /// <summary>Gets the 1-based column of the error (in UTF-16 code units).</summary>
    public int Column { get; }
}
