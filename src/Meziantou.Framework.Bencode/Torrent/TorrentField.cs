namespace Meziantou.Framework.Bencode.Torrent;

internal static class TorrentField
{
    /// <summary>Gets the encoding the 'encoding' key names, or <see langword="null"/> for UTF-8 and for names that are not known.</summary>
    /// <remarks>
    /// The encoding throws on bytes it cannot decode and characters it cannot encode, so a text field is never silently
    /// altered. Code pages come from <see cref="CodePagesEncodingProvider"/> without registering it, so this does not change
    /// the encodings of the process.
    /// </remarks>
    public static Encoding? ResolveEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = name.Trim();
        Encoding encoding;
        try
        {
            encoding = CodePagesEncodingProvider.Instance.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                ?? Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }

        return encoding.CodePage is 65001 ? null : encoding;
    }

    /// <summary>Decodes a metainfo field, reporting invalid data as a <see cref="FormatException"/> like every other malformed field.</summary>
    /// <remarks>
    /// Bencode strings hold arbitrary bytes. Without this, decoding throws <see cref="DecoderFallbackException"/>, which
    /// escapes <see cref="TorrentFile.TryParse"/> because it is not a <see cref="FormatException"/>.
    /// </remarks>
    public static string ToText(BencodeString value, string fieldName, Encoding? encoding)
    {
        try
        {
            return encoding is null ? value.ToUtf8String() : encoding.GetString(value.Value.Span);
        }
        catch (DecoderFallbackException ex)
        {
            throw new FormatException($"The '{fieldName}' field is not valid {encoding?.WebName ?? "UTF-8"}.", ex);
        }
    }

    public static BencodeString FromText(string value, string fieldName, Encoding? encoding)
    {
        try
        {
            return new BencodeString(encoding is null ? Encoding.UTF8.GetBytes(value) : encoding.GetBytes(value));
        }
        catch (EncoderFallbackException ex)
        {
            throw new FormatException($"The '{fieldName}' field cannot be encoded with {encoding?.WebName}.", ex);
        }
    }

    public static long ToInt64(BencodeInteger value, string fieldName)
    {
        if (!value.TryGetInt64(out var result))
            throw new FormatException($"The '{fieldName}' field is out of range.");

        return result;
    }

    public static BencodeString CreateKey(string value) => new(Encoding.UTF8.GetBytes(value));

    public static HashSet<BencodeString> CreateKeySet(params BencodeString[] keys) => [.. keys];

    /// <summary>Copies the entries whose key the model does not represent.</summary>
    public static void ReadAdditionalKeys(BencodeDictionary dictionary, HashSet<BencodeString> modeledKeys, IDictionary<BencodeString, BencodeValue> additionalKeys)
    {
        foreach (var entry in dictionary)
        {
            if (!modeledKeys.Contains(entry.Key))
            {
                additionalKeys.Add(entry.Key, entry.Value);
            }
        }
    }

    public static void WriteAdditionalKeys(BencodeDictionary dictionary, IDictionary<BencodeString, BencodeValue> additionalKeys, HashSet<BencodeString> modeledKeys)
    {
        foreach (var entry in additionalKeys)
        {
            if (modeledKeys.Contains(entry.Key))
                throw new FormatException($"The additional key '{entry.Key}' is a key of the model. Set its property instead.");

            dictionary.Add(entry.Key, entry.Value);
        }
    }
}
