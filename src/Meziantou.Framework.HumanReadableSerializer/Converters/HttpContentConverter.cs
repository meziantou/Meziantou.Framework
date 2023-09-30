using System.Diagnostics;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class HttpContentConverter : HumanReadableConverter<HttpContent>
{
    private static readonly HashSet<string> TextMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/ecmascript",
        "application/javascript",
        "application/json",
        "application/x-ecmascript",
        "application/x-javascript",
        "application/x-www-form-urlencoded",
        "application/xml",
    };

    protected override void WriteValue(HumanReadableTextWriter writer, HttpContent? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value != null);

        writer.StartObject();

        var hasHeaders = value.Headers.Any();
        var hasMultipleContent = value is IEnumerable<HttpContent>;
        if (hasHeaders)
        {
            writer.WritePropertyName("Headers");
            HumanReadableSerializer.Serialize(writer, value.Headers, options);
        }

        if (hasHeaders || hasMultipleContent)
            writer.WritePropertyName("Value");

        if (value is IEnumerable<HttpContent> collection)
        {
            options.GetConverter(typeof(IEnumerable<HttpContent>)).WriteValue(writer, collection, options);
        }
        else
        {
            if (CanReadAsString(value))
            {
                var str = value.ReadAsStringAsync().Result;

                var mediaType = value.Headers.ContentType?.MediaType;
                if (mediaType != null)
                {
                    str = options.FormatValue(mediaType, str);
                }

                writer.WriteValue(str);
            }
            else
            {
                var bytes = value.ReadAsByteArrayAsync().Result;
                options.GetConverter(typeof(byte[])).WriteValue(writer, bytes, options);
            }
        }
        writer.EndObject();
    }

    private static bool CanReadAsString(HttpContent content)
    {
        if (content is StringContent or FormUrlEncodedContent)
            return true;

#if NET5_0_OR_GREATER
        if (content is System.Net.Http.Json.JsonContent)
            return true;
#endif

        var charSet = content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrEmpty(charSet))
            return true;

        var mimeType = content.Headers.ContentType?.MediaType;
        if (mimeType != null)
        {
            // https://www.iana.org/assignments/media-types/media-types.xhtml
            if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                return true;

            if (mimeType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) || mimeType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
                return true;

            if (TextMimeTypes.Contains(mimeType))
                return true;
        }

        return false;
    }
}
