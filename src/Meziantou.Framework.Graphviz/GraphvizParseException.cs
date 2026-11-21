namespace Meziantou.Framework.Graphviz;

/// <summary>The exception thrown when a DOT text is not valid.</summary>
public sealed class GraphvizParseException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="GraphvizParseException"/> class.</summary>
    public GraphvizParseException()
        : this(new GraphvizParseError("The DOT text is not valid.", 1, 1))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GraphvizParseException"/> class.</summary>
    /// <param name="message">The description of the error.</param>
    public GraphvizParseException(string message)
        : this(new GraphvizParseError(message, 1, 1))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GraphvizParseException"/> class.</summary>
    /// <param name="message">The description of the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public GraphvizParseException(string message, Exception? innerException)
        : base(message, innerException)
    {
        Error = new GraphvizParseError(message, 1, 1);
    }

    /// <summary>Initializes a new instance of the <see cref="GraphvizParseException"/> class.</summary>
    /// <param name="error">The error.</param>
    public GraphvizParseException(GraphvizParseError error)
        : base(error.ToString())
    {
        Error = error;
    }

    /// <summary>Gets the error, with its position.</summary>
    public GraphvizParseError Error { get; }
}
