using System.Text;

namespace Meziantou.Framework.Pdf.Security;

/// <summary>
/// The preparation of the passwords of revision 6 (ISO 32000-2, 7.6.4.3.3): the SASLprep profile of stringprep
/// (RFC 4013), then UTF-8, truncated to 127 bytes (even inside a character). The normalization is the managed one of Meziantou.Framework.Unicode,
/// so that it does not depend on ICU.
/// </summary>
internal static class SaslPrep
{
    private const int MaxLength = 127;

    public static byte[] GetBytes(string password)
    {
        var mapped = new StringBuilder(password.Length);
        foreach (var c in password)
        {
            // RFC 4013, 2.1: the non-ASCII spaces (C.1.2) are mapped to a space, and the characters "commonly mapped to
            // nothing" (B.1) are removed
            if (IsNonAsciiSpace(c))
            {
                mapped.Append(' ');
            }
            else if (!IsMappedToNothing(c))
            {
                mapped.Append(c);
            }
        }

        var normalized = UnicodeNormalization.Normalize(mapped.ToString(), NormalizationForm.FormKC);
        var bytes = Encoding.UTF8.GetBytes(normalized);

        // 7.6.4.3.3: the UTF-8 string is truncated to 127 bytes, even in the middle of a character, as the other readers
        // and writers do (qpdf, Acrobat): cutting on a character boundary would derive another key
        return bytes.Length <= MaxLength ? bytes : bytes[..MaxLength];
    }

    private static bool IsNonAsciiSpace(char c) => c is '\u00A0' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u202F' or '\u205F' or '\u3000';

    private static bool IsMappedToNothing(char c) => c is '\u00AD' or '\u034F' or '\u1806' or (>= '\u180B' and <= '\u180D') or (>= '\u200B' and <= '\u200D') or '\u2060' or (>= '\uFE00' and <= '\uFE0F') or '\uFEFF';
}
