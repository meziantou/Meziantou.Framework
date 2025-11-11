namespace Meziantou.Framework.Templating;

/// <summary>Represents errors that occur during template processing, compilation, or execution.</summary>
public class TemplateException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TemplateException"/> class.</summary>
    public TemplateException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TemplateException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public TemplateException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TemplateException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public TemplateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
