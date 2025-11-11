namespace Meziantou.Framework.Html;

/// <summary>
/// Represents errors that occur during HTML parsing or manipulation.
/// </summary>
#if HTML_PUBLIC
public
#else
[SuppressMessage("Design", "CA1064:Exceptions should be public")]
internal
#endif
sealed class HtmlException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="HtmlException"/> class with a default message.</summary>
    public HtmlException()
        : base("HTML0001: Html exception")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HtmlException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public HtmlException(string? message)
    : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HtmlException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public HtmlException(string? message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Extracts the error code from an exception message.</summary>
    /// <param name="message">The exception message in the format "HTMLnnnn: description".</param>
    /// <returns>The numeric error code, or -1 if the message doesn't contain a valid error code.</returns>
    /// <example>
    /// <code>
    /// var code = HtmlException.GetCode("HTML0004: Html encoding mismatch error");
    /// // code == 4
    /// </code>
    /// </example>
    public static int GetCode(string? message)
    {
        if (message is null)
            return -1;

        const string Prefix = "HTML";

        if (!message.StartsWith(Prefix, StringComparison.Ordinal))
            return -1;

        var pos = message.IndexOf(':', Prefix.Length);
        if (pos < 0)
            return -1;

        if (int.TryParse(message[Prefix.Length..pos], NumberStyles.None, CultureInfo.InvariantCulture, out var i))
            return i;

        return -1;
    }

    /// <summary>Gets the numeric error code from the exception message.</summary>
    /// <value>The error code, or -1 if no valid code is present.</value>
    public int Code => GetCode(Message);
}
