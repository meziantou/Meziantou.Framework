using System.Text.Json;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Provides helper methods for working with HAR post data payloads.</summary>
public static class HarPostDataExtensions
{
    /// <summary>Default extension field name used to store the post data encoding.</summary>
    public const string DefaultEncodingExtensionName = "x-meziantou-encoding";

    /// <summary>Gets the raw payload bytes represented by the HAR post data.</summary>
    /// <param name="postData">The HAR post data.</param>
    /// <param name="rawData">The decoded raw bytes if available.</param>
    /// <param name="encodingExtensionName">The extension field name containing the encoding metadata.</param>
    /// <returns>
    /// <see langword="true" /> when payload bytes are available; otherwise, <see langword="false" />.
    /// Returns <see langword="false" /> when the text is not valid for the declared encoding, or when the
    /// declared encoding is not understood, rather than reporting the undecoded text as the payload.
    /// </returns>
    public static bool TryGetRawData(this HarPostData? postData, [NotNullWhen(true)] out byte[]? rawData, string encodingExtensionName = DefaultEncodingExtensionName)
    {
        if (postData?.Text is null)
        {
            rawData = null;
            return false;
        }

        if (TryGetEncoding(postData, encodingExtensionName, out var encoding))
        {
            if (!string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                rawData = null;
                return false;
            }

            return TryDecodeBase64(postData.Text, out rawData);
        }

        rawData = Encoding.UTF8.GetBytes(postData.Text);
        return true;
    }

    private static bool TryDecodeBase64(string text, [NotNullWhen(true)] out byte[]? bytes)
    {
        var buffer = new byte[((text.Length / 4) + 1) * 3];
        if (Convert.TryFromBase64String(text, buffer, out var written))
        {
            bytes = buffer.AsSpan(0, written).ToArray();
            return true;
        }

        bytes = null;
        return false;
    }

    private static bool TryGetEncoding(HarPostData postData, string encodingExtensionName, out string? encoding)
    {
        if (postData.ExtensionData is not null &&
            postData.ExtensionData.TryGetValue(encodingExtensionName, out var value) &&
            value.ValueKind is JsonValueKind.String)
        {
            encoding = value.GetString();
            return true;
        }

        encoding = null;
        return false;
    }
}
