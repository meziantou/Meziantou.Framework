using System.Text;

namespace Meziantou.Framework.MediaTags.Internals;

/// <summary>
/// Helper for ISO-8859-1 (Latin-1) encoding, commonly used in ID3v1 and ID3v2 tags.
/// </summary>
internal static class Latin1Encoding
{
    private static Encoding? s_encoding;

    public static Encoding Instance => s_encoding ??= Encoding.GetEncoding(28591);

    public static string GetString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return string.Empty;

        return Instance.GetString(bytes);
    }

    public static byte[] GetBytes(string value)
    {
        return Instance.GetBytes(value);
    }
}
