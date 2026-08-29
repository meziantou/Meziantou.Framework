namespace Meziantou.Framework.Bencode.Torrent;

internal static class TorrentField
{
    /// <summary>Decodes a metainfo field as UTF-8, reporting invalid data as a <see cref="FormatException"/> like every other malformed field.</summary>
    /// <remarks>
    /// Bencode strings hold arbitrary bytes, and torrents produced with a legacy code page carry text that is not
    /// valid UTF-8. Without this, decoding throws <see cref="DecoderFallbackException"/>, which escapes
    /// <see cref="TorrentFile.TryParse"/> because it is not a <see cref="FormatException"/>.
    /// </remarks>
    public static string ToText(BencodeString value, string fieldName)
    {
        try
        {
            return value.ToUtf8String();
        }
        catch (DecoderFallbackException ex)
        {
            throw new FormatException($"The '{fieldName}' field is not valid UTF-8.", ex);
        }
    }
}
