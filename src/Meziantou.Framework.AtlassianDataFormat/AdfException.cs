namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>The exception thrown when a document cannot be parsed or converted.</summary>
public sealed class AdfException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AdfException"/> class.</summary>
    public AdfException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AdfException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public AdfException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AdfException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public AdfException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
