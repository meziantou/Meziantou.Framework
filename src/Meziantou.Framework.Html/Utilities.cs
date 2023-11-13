using System.Diagnostics.Contracts;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Html;

internal static class Utilities
{
    [Pure]
    public static bool EqualsIgnoreCase(this string? str1, string? str2)
    {
        return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
    }

    [Pure]
    public static string? Nullify(string? str, bool trim)
    {
        if (str is null)
            return null;

        if (trim)
        {
            str = str.Trim();
        }

        if (string.IsNullOrEmpty(str))
            return null;

        return str;
    }

    [Pure]
    public static bool StartsWith(this string str, char c)
    {
        if (str.Length == 0)
            return false;

        return str[0] == c;
    }

    [Pure]
    public static bool EndsWith(this string str, char c)
    {
        if (str.Length == 0)
            return false;

        return str[^1] == c;
    }

    public static Encoding GetDefaultEncoding()
    {
        return Encoding.GetEncoding(0);
    }

    public static StreamReader OpenReader(string filePath)
    {
        var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 0x400, leaveOpen: false);
    }

    public static StreamReader OpenReader(string filePath, Encoding encoding)
    {
        var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, 0x400, leaveOpen: false);
    }

    public static StreamReader OpenReader(string filePath, Encoding encoding, bool detectEncodingFromByteOrderMarks)
    {
        var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks, 0x400, leaveOpen: false);
    }

    public static StreamReader OpenReader(string filePath, bool detectEncodingFromByteOrderMarks)
    {
        var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks, 0x400, leaveOpen: false);
    }

    public static StreamReader OpenReader(string filePath, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
    {
        var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks, bufferSize, leaveOpen: false);
    }

    public static StreamWriter OpenWriter(string filePath, bool append, Encoding encoding, int bufferSize)
    {
        var stream = File.Open(filePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        if (append)
        {
            stream.Seek(0, SeekOrigin.End);
        }
        return new StreamWriter(stream, encoding, bufferSize, leaveOpen: false);
    }

    public static StreamWriter OpenWriter(string filePath, bool append, Encoding encoding)
    {
        var stream = File.Open(filePath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read);
        if (append)
        {
            stream.Seek(0, SeekOrigin.End);
        }
        return new StreamWriter(stream, encoding, 0x400, leaveOpen: false);
    }

    public static StreamWriter OpenWriter(string filePath)
    {
        var stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        return new StreamWriter(stream, encoding, 0x400, leaveOpen: false);
    }

    public static string? GetAttributeFromHeader(string? header, string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        int index;
        if (header is null)
            return null;

        var startIndex = 1;
        while (startIndex < header.Length)
        {
            startIndex = CultureInfo.InvariantCulture.CompareInfo.IndexOf(header, name, startIndex, CompareOptions.IgnoreCase);
            if (startIndex < 0 || (startIndex + name.Length) >= header.Length)
                break;

            var c1 = header[startIndex - 1];
            var c2 = header[startIndex + name.Length];
            if ((c1 == ';' || c1 == ',' || char.IsWhiteSpace(c1)) && (c2 == '=' || char.IsWhiteSpace(c2)))
                break;

            startIndex += name.Length;
        }

        if (startIndex < 0 || startIndex >= header.Length)
            return null;

        startIndex += name.Length;
        while (startIndex < header.Length && char.IsWhiteSpace(header[startIndex]))
        {
            startIndex++;
        }

        if (startIndex >= header.Length || header[startIndex] != '=')
            return null;

        startIndex++;
        while (startIndex < header.Length && char.IsWhiteSpace(header[startIndex]))
        {
            startIndex++;
        }

        if (startIndex >= header.Length)
            return null;

        if (startIndex < header.Length && header[startIndex] == '"')
        {
            if (startIndex == (header.Length - 1))
                return null;

            index = header.IndexOf('"', startIndex + 1);
            if (index < 0 || index == (startIndex + 1))
                return null;

            return header.Substring(startIndex + 1, index - startIndex - 1).Trim();
        }
        index = startIndex;
        while (index < header.Length)
        {
            if (header[index] == ' ' || header[index] == ',')
                break;

            index++;
        }

        if (index == startIndex)
            return null;

        return header[startIndex..index].Trim();
    }

    public static string GetValidXmlName(string text)
    {
        if ((text is null) || (text.Trim().Length == 0))
            throw new ArgumentNullException(nameof(text));

        var sb = new StringBuilder(text.Length);
        if (IsValidXmlNameStart(text[0]))
        {
            sb.Append(text[0]);
        }
        else
        {
            sb.Append(GetXmlNameEscape(text[0]));
        }
        for (var i = 1; i < text.Length; i++)
        {
            if (IsValidXmlNamePart(text[i]))
            {
                sb.Append(text[i]);
            }
            else
            {
                sb.Append(GetXmlNameEscape(text[i]));
            }
        }
        return sb.ToString();
    }

    private static string GetXmlNameEscape(char c)
    {
        return "_x" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) + "_";
    }

    // http://www.w3.org/TR/REC-xml/#NT-Letter
    // valids are Lu, Ll, Lt, Lo, Nl
    private static bool IsValidXmlNameStart(char c)
    {
        if (c == '_')
            return true;

        if ((c == 0x20DD) || (c == 0x20E0))
            return false;

        if ((c > 0xF900) && (c < 0xFFFE))
            return false;

        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category == UnicodeCategory.UppercaseLetter
            || category == UnicodeCategory.LowercaseLetter
            || category == UnicodeCategory.TitlecaseLetter
            || category == UnicodeCategory.LetterNumber
            || category == UnicodeCategory.OtherLetter;
    }

    // valids are Lu, Ll, Lt, Lo, Nl, Mc, Me, Mn, Lm, or Nd
    [SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "Better readability")]
    private static bool IsValidXmlNamePart(char c)
    {
        if ((c == '_') || (c == '.'))
            return true;

        if (c == 0x0387)
            return true;

        if ((c == 0x20DD) || (c == 0x20E0))
            return false;

        if ((c > 0xF900) && (c < 0xFFFE))
            return false;

        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        switch (category)
        {
            case UnicodeCategory.UppercaseLetter://Lu
            case UnicodeCategory.LowercaseLetter://Ll
            case UnicodeCategory.TitlecaseLetter://Lt
            case UnicodeCategory.LetterNumber://Nl
            case UnicodeCategory.OtherLetter://Lo
            case UnicodeCategory.ModifierLetter://Lm
            case UnicodeCategory.NonSpacingMark://Mn
            case UnicodeCategory.SpacingCombiningMark://Mc
            case UnicodeCategory.EnclosingMark://Me
            case UnicodeCategory.DecimalDigitNumber://Nd
                return true;

            default:
                return false;
        }
    }

    public static string? GetServerPath(string path)
    {
        return GetServerPath(path, out _, out _, out _);
    }

    public static string? GetServerPath(string path, out string? serverName, out string? shareName, out string? sharePath)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        serverName = null;
        shareName = null;
        sharePath = null;
        if (path.Length < 5 || !path.StartsWith(@"\\", StringComparison.Ordinal)) // min is \\x\y (5 chars)
            return null;

        if (path[2] == Path.DirectorySeparatorChar)
            return null;

        var pos = path.IndexOf(Path.DirectorySeparatorChar, 3); // \\\ is invalid
        if (pos < 0)
        {
            serverName = path[2..];
            return path;
        }

        var pos2 = path.IndexOf(Path.DirectorySeparatorChar, pos + 2); // \\server\\ is invalid
        if (pos2 < 0)
        {
            serverName = path[2..pos];
            shareName = path[(pos + 1)..];
            return path;
        }

        serverName = path[2..pos];
        shareName = path[(pos + 1)..pos2];
        sharePath = path[(pos2 + 1)..];
        return @"\\" + serverName + @"\" + shareName;
    }

    private const string Prefix = @"\\?\";

    public static bool IsRooted(string? path)
    {
        if (path is null)
            return false;

        if (path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            path = path[Prefix.Length..];
        }

        var spath = GetServerPath(path);
        if (spath is not null)
            return true;

        if (path.Length >= 1 && path[0] == Path.DirectorySeparatorChar)
            return true;

        if (path.Length >= 2 && path[1] == Path.VolumeSeparatorChar)
            return true;

        return false;
    }

    public static string EnsureTerminatingSeparator(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Path.DirectorySeparatorChar.ToString(CultureInfo.InvariantCulture);

        if (path[^1] != Path.DirectorySeparatorChar)
            return path + Path.DirectorySeparatorChar;

        return path;
    }
}
