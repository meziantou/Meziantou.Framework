using System.Diagnostics;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class HttpHeadersConverter : HttpHeadersConverter<HttpHeaders>
{
    public HttpHeadersConverter()
        : base(excludedHeaderNames: null, headerFormatters: null)
    {
    }
}

internal class HttpHeadersConverter<T> : HumanReadableConverter<T> where T : HttpHeaders
{
    private readonly HashSet<string>? _excludedHeaderNames;
    private readonly HttpHeaderValueFormatter[] _headerFormatters;

    public HttpHeadersConverter(IEnumerable<string>? excludedHeaderNames, IEnumerable<HttpHeaderValueFormatter> headerFormatters)
    {
        if (excludedHeaderNames != null)
            _excludedHeaderNames = new HashSet<string>(excludedHeaderNames, StringComparer.OrdinalIgnoreCase);

        _headerFormatters = headerFormatters?.ToArray() ?? Array.Empty<HttpHeaderValueFormatter>();
    }

    protected override void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);
        var hasValue = false;

#if NETSTANDARD2_0 || NETFRAMEWORK
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> values = value;
#else
        IEnumerable<KeyValuePair<string, HeaderStringValues>> values = value.NonValidated;
#endif

        if (options.PropertyOrder != null)
        {
            values = values.OrderBy(o => o.Key, options.PropertyOrder);
        }

        foreach (var header in values)
        {
            if (_excludedHeaderNames != null && _excludedHeaderNames.Contains(header.Key))
                continue;

            if (!hasValue)
            {
                writer.StartObject();
                hasValue = true;
            }

            writer.WritePropertyName(header.Key);

#if NETSTANDARD2_0 || NETFRAMEWORK
            var headerValueCount = header.Value.Count();
#else
            var headerValueCount = header.Value.Count;
#endif
            if (headerValueCount == 0)
                writer.WriteValue("");
            else if (headerValueCount == 1)
            {
                WriteValueHeader(writer, header.Key, header.Value.First());
            }
            else
            {
                writer.StartArray();
                foreach (var headerValue in header.Value)
                {
                    writer.StartArrayItem();
                    WriteValueHeader(writer, header.Key, headerValue);
                    writer.EndArrayItem();
                }

                writer.EndArray();
            }
        }

        if (hasValue)
            writer.EndObject();
        else
        {
            writer.WriteEmptyObject();
        }
    }

    private void WriteValueHeader(HumanReadableTextWriter writer, string headerName, string value)
    {
        foreach (var formatter in _headerFormatters)
        {
            value = formatter.FormatHeaderValue(headerName, value);
        }

        writer.WriteValue(value);
    }
}
