using System.Net.Http.Headers;

namespace Meziantou.Framework.HumanReadable.Converters;

// The boundary of a multipart content is random by default, and the parts are serialized separately anyway
internal sealed class MultipartBoundaryFormatter : HttpHeaderValueFormatter
{
    public static MultipartBoundaryFormatter Instance { get; } = new();

    public override string FormatHeaderValue(string headerName, string headerValue)
    {
        if (!string.Equals(headerName, "Content-Type", StringComparison.OrdinalIgnoreCase) || !MediaTypeHeaderValue.TryParse(headerValue, out var mediaType))
            return headerValue;

        var boundary = mediaType.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, "boundary", StringComparison.OrdinalIgnoreCase));
        if (boundary is null)
            return headerValue;

        mediaType.Parameters.Remove(boundary);
        return mediaType.ToString();
    }
}
