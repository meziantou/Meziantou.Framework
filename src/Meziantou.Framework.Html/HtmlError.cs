#nullable disable

namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
internal
#endif
sealed class HtmlError
{
    public HtmlError(HtmlReaderState state, HtmlErrorType errorType)
    {
        Line = state.Line;
        Column = state.Column;
        Offset = state.Offset;
        ErrorType = errorType;
    }

    public HtmlError(int line, int column, int offset, HtmlErrorType errorType)
    {
        Line = line;
        Column = column;
        Offset = offset;
        ErrorType = errorType;
    }

    public HtmlNode Node { get; internal set; }
    public HtmlErrorType ErrorType { get; }
    public int Offset { get; }
    public int Line { get; }
    public int Column { get; }

    public override string ToString()
    {
#if NET6_0_OR_GREATER
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Line}x{Column}x{Offset} {ErrorType}");
#else
        return FormattableString.Invariant($"{Line}x{Column}x{Offset} {ErrorType}");
#endif
    }
}
