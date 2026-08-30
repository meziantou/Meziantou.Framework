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
        Debug.Assert(value is not null);

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
            options.GetConverter(typeof(IEnumerable<HttpContent>)).WriteValue(writer, collection, typeof(IEnumerable<HttpContent>), options);
        }
        else
        {
            if (CanReadAsString(value))
            {
                var str = ReadSynchronously(value.ReadAsStringAsync);

                var mediaType = value.Headers.ContentType?.MediaType;
                if (mediaType is not null)
                {
                    writer.WriteFormattedValue(mediaType, str);
                }
                else
                {
                    writer.WriteValue(str);
                }
            }
            else
            {
                var bytes = ReadSynchronously(value.ReadAsByteArrayAsync);
                options.GetConverter(typeof(byte[])).WriteValue(writer, bytes, typeof(byte[]), options);
            }
        }

        writer.EndObject();
    }

    // The serializer is synchronous, so the content has to be drained on the calling thread.
    // Task.Run detaches the read from the ambient SynchronizationContext: without it, content
    // whose continuations post back to a single-threaded context (a UI thread, or a custom
    // Stream in a test fixture) deadlocks against the thread blocked here.
    // ConfigureAwait(false) is not enough, as it only applies to the awaits owned by this method.
    private static T ReadSynchronously<T>(Func<Task<T>> read)
    {
        if (SynchronizationContext.Current is null)
            return read().GetAwaiter().GetResult();

        return Task.Run(read).GetAwaiter().GetResult();
    }

    private static bool CanReadAsString(HttpContent content)
    {
        if (content is StringContent or FormUrlEncodedContent)
            return true;

        if (content is System.Net.Http.Json.JsonContent)
            return true;

        var charSet = content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrEmpty(charSet))
            return true;

        var mimeType = content.Headers.ContentType?.MediaType;
        if (mimeType is not null)
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
