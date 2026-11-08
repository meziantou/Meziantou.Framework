namespace Meziantou.Framework.WebPlatform;

/// <summary>The exception thrown by the <see cref="HtmlDocumentParser"/> when the markup exceeds a limit of the <see cref="HtmlParserOptions"/>, such as <see cref="HtmlParserOptions.MaxElementCount"/>.</summary>
public sealed class HtmlParserLimitExceededException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="HtmlParserLimitExceededException"/> class.</summary>
    public HtmlParserLimitExceededException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HtmlParserLimitExceededException"/> class with a message.</summary>
    public HtmlParserLimitExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HtmlParserLimitExceededException"/> class with a message and the exception that caused it.</summary>
    public HtmlParserLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
