namespace Meziantou.Framework;

/// <summary>Exception thrown when a URL pattern is invalid or cannot be parsed.</summary>
public sealed class UrlPatternException : Exception
{
    public UrlPatternException()
    {
    }

    public UrlPatternException(string? message)
        : base(message)
    {
    }

    public UrlPatternException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
