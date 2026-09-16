using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;

// A snapshot of the header options, so changes made to the options after AddHttpConverters have no effect
internal sealed class HttpHeaderSettings
{
    public HttpHeaderSettings(IEnumerable<string> excludedHeaderNames, IEnumerable<HttpHeaderValueFormatter> formatters)
    {
        ExcludedHeaderNames = new HashSet<string>(excludedHeaderNames, StringComparer.OrdinalIgnoreCase);
        Formatters = [.. formatters];
    }

    public HashSet<string> ExcludedHeaderNames { get; }
    public HttpHeaderValueFormatter[] Formatters { get; }

    public bool IsEmpty => ExcludedHeaderNames.Count is 0 && Formatters.Length is 0;

    // Returns true when no header would be written
    public bool IsEmptyAfterFiltering(object? value)
    {
        if (value is HttpHeaders headers)
        {
            foreach (var header in headers.NonValidated)
            {
                if (!ExcludedHeaderNames.Contains(header.Key))
                    return false;
            }
        }

        return true;
    }
}
