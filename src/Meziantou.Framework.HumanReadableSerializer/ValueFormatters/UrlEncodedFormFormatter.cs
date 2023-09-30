using System.Text;

namespace Meziantou.Framework.HumanReadable.ValueFormatters;

internal sealed class UrlEncodedFormFormatter : ValueFormatter
{
    private readonly UrlEncodedFormFormatterOptions _options;

    public UrlEncodedFormFormatter(UrlEncodedFormFormatterOptions options)
    {
        _options = options;
    }

    public override string Format(string value)
    {
        if (!_options.PrettyFormat)
            return value;

        try
        {
            var sb = new StringBuilder();

            var span = value.AsSpan();
            while (!span.IsEmpty)
            {
                var indexOf = span.IndexOf('=');
                if (indexOf == -1)
                    return value; // invalid

                AppendValue(sb, span[..indexOf]);
                sb.Append(": ");
                span = span[(indexOf + 1)..];

                indexOf = span.IndexOf('&');
                if (indexOf == -1)
                {
                    AppendValue(sb, span);
                    break;
                }
                else
                {
                    AppendValue(sb, span[..indexOf]);
                    sb.AppendLine();
                    span = span[(indexOf + 1)..];
                }
            }

            return sb.ToString();
        }
        catch
        {
        }

        return value;
    }

    private static void AppendValue(StringBuilder stringBuilder, ReadOnlySpan<char> value)
    {
        var str = value.ToString();
        str = str.Replace('+', ' ');
        stringBuilder.Append(Uri.UnescapeDataString(str));
    }
}
