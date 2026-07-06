namespace Meziantou.Framework.Yaml;

/// <summary>Base exception that is thrown when the a problem occurs in the Meziantou.Framework.Yaml library.</summary>
public class YamlException : Exception
{
    /// <summary>Gets the optional source name associated with the YAML payload (for example, a file path).</summary>
    public string? SourceName { get; }

    /// <summary>Gets the position in the input stream where the event that originated the exception starts.</summary>
    public Mark Start { get; }

    /// <summary>Gets the position in the input stream where the event that originated the exception ends.</summary>
    public Mark End { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlException"/> class.
    /// </summary>
    public YamlException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public YamlException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlException"/> class.
    /// </summary>
    public YamlException(Mark start, Mark end, string message)
        : this(start, end, message, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlException"/> class.
    /// </summary>
    public YamlException(Mark start, Mark end, string message, Exception? innerException)
        : this(null, start, end, message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlException"/> class.
    /// </summary>
    public YamlException(string? sourceName, Mark start, Mark end, string message)
        : this(sourceName, start, end, message, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlException"/> class.
    /// </summary>
    public YamlException(string? sourceName, Mark start, Mark end, string message, Exception? innerException)
        : base(FormatMessage(sourceName, start, end, message), innerException)
    {
        SourceName = sourceName;
        Start = start;
        End = end;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="YamlException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The inner.</param>
    public YamlException(string message, Exception inner)
        : base(message, inner)
    {
    }

    private static string FormatMessage(string? sourceName, Mark start, Mark end, string message)
    {
        if (string.IsNullOrEmpty(sourceName))
        {
            return $"({start}) - ({end}): {message}";
        }

        return $"{sourceName}: ({start}) - ({end}): {message}";
    }
}