namespace Meziantou.Framework.Yaml;

/// <summary>Exception that is thrown when a semantic error is detected on a YAML stream.</summary>
public class SemanticErrorException : YamlException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticErrorException"/> class.
    /// </summary>
    public SemanticErrorException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticErrorException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public SemanticErrorException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticErrorException"/> class.
    /// </summary>
    public SemanticErrorException(Mark start, Mark end, string message)
        : base(start, end, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticErrorException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The inner.</param>
    public SemanticErrorException(string message, Exception inner)
        : base(message, inner)
    {
    }
}