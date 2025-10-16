namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
[SuppressMessage("Design", "CA1064:Exceptions should be public")]
internal
#endif
sealed class HtmlException : Exception
{
    public HtmlException()
        : base("HTML0001: Html exception")
    {
    }

    public HtmlException(string? message)
        : base(message)
    {
    }

    public HtmlException(string? message, Exception innerException)
        : base(message, innerException)
    {
    }

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

    public int Code => GetCode(Message);
}
