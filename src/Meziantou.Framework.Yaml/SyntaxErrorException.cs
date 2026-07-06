namespace Meziantou.Framework.Yaml;

/// <summary>Exception that is thrown when a syntax error is detected on a YAML stream.</summary>
public class SyntaxErrorException : YamlException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyntaxErrorException"/> class.
    /// </summary>
    public SyntaxErrorException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SyntaxErrorException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public SyntaxErrorException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SyntaxErrorException"/> class.
    /// </summary>
    public SyntaxErrorException(Mark start, Mark end, string message)
        : base(start, end, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SyntaxErrorException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The inner.</param>
    public SyntaxErrorException(string message, Exception inner)
        : base(message, inner)
    {
    }
}