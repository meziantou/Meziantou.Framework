namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>A parse error whose position is computed only when requested.</summary>
internal readonly struct HtmlParseErrorInfo(string code, string input, int offset)
{
    public string Code => code;

    public HtmlParseError ToParseError()
    {
        var line = 1;
        var column = 1;
        var end = Math.Min(offset, input.Length);
        for (var i = 0; i < end; i++)
        {
            var c = input[i];
            if (c == '\n' || (c == '\r' && (i + 1 >= input.Length || input[i + 1] != '\n')))
            {
                line++;
                column = 1;
            }
            else if (c != '\r')
            {
                // Columns count UTF-16 code units
                column++;
            }
        }

        return new HtmlParseError(code, offset, line, column);
    }
}
