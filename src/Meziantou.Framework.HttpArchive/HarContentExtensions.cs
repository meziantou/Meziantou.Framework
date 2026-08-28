namespace Meziantou.Framework.HttpArchive;

/// <summary>Provides helper methods for working with HAR response content payloads.</summary>
public static class HarContentExtensions
{
    /// <summary>Gets the raw payload bytes represented by the HAR content.</summary>
    /// <param name="content">The HAR content.</param>
    /// <param name="rawData">The decoded raw bytes if available.</param>
    /// <returns>
    /// <see langword="true" /> when payload bytes are available; otherwise, <see langword="false" />.
    /// Returns <see langword="false" /> when the text is not valid for the declared
    /// <see cref="HarContent.Encoding"/>, or when that encoding is not understood, rather than reporting the
    /// undecoded text as the payload.
    /// </returns>
    public static bool TryGetRawData(this HarContent? content, [NotNullWhen(true)] out byte[]? rawData)
    {
        if (content?.Text is null)
        {
            rawData = null;
            return false;
        }

        if (!string.IsNullOrEmpty(content.Encoding))
        {
            if (!string.Equals(content.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
            {
                rawData = null;
                return false;
            }

            return TryDecodeBase64(content.Text, out rawData);
        }

        rawData = Encoding.UTF8.GetBytes(content.Text);
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
}
