using System.Text;

namespace Meziantou.Framework.Email.Internals;

internal static class MimeWriter
{
    /// <summary>Writes headers: the headers read from a message as they were, the others folded at 78 characters ([RFC 5322] 2.2.3), then the empty line.</summary>
    public static void WriteHeaders(Stream stream, IEnumerable<MimeHeader> headers, bool endOfHeaders = true)
    {
        foreach (var header in headers)
        {
            if (header.RawBytes is { } raw)
            {
                stream.Write(raw);
                EnsureLineBreak(stream, raw);
                continue;
            }

            stream.Write(Encoding.UTF8.GetBytes(Fold(header.Name + ": " + header.RawValue)));
            stream.Write("\r\n"u8);
        }

        if (endOfHeaders)
        {
            stream.Write("\r\n"u8);
        }
    }

    /// <summary>Folds a header line at the whitespace before the 78th character, when there is one.</summary>
    public static string Fold(string line)
    {
        if (line.Length <= 78)
        {
            return line;
        }

        var builder = new StringBuilder(line.Length + 16);
        var lineStart = 0;
        while (line.Length - lineStart > 78)
        {
            var fold = line.LastIndexOf(" ", lineStart + 77, 77, StringComparison.Ordinal);
            if (fold <= lineStart)
            {
                // No whitespace: fold at the next one, the line may be longer than 78 characters
                fold = line.IndexOf(" ", lineStart + 78, StringComparison.Ordinal);
                if (fold < 0)
                {
                    break;
                }
            }

            builder.Append(line, lineStart, fold - lineStart).Append("\r\n");
            lineStart = fold;
        }

        return builder.Append(line, lineStart, line.Length - lineStart).ToString();
    }

    public static void EnsureLineBreak(Stream stream, ReadOnlySpan<byte> written)
    {
        if (written.Length == 0 || written[^1] != '\n')
        {
            stream.Write("\r\n"u8);
        }
    }
}
