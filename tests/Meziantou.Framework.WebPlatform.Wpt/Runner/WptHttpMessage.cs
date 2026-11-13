using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The HTTP/1.1 framing of the messages that wptserve writes on the connection, as the network layer of a browser reads them
/// (https://www.rfc-editor.org/rfc/rfc9112.html): the raw responses that a handler writes itself
/// (<c>response.writer.write()</c>), the line breaks that a header value writes on the wire, and the <c>Content-Length</c> of
/// a response (https://fetch.spec.whatwg.org/#concept-header-extract-length).
/// </summary>
internal static class WptHttpMessage
{
    /// <summary>A response read from the connection: its status line, its headers and the bytes of its body that were received.</summary>
    internal sealed record Message(int Status, string StatusText, List<KeyValuePair<string, string>> Headers, byte[] Body);

    /// <summary>
    /// Returns the value of a header that wptserve writes as it is (<c>name: value\r\n</c>) as the HTTP parser of a browser
    /// reads it: a line break followed by a space or a tab is an obsolete line folding, replaced with a space
    /// (https://www.rfc-editor.org/rfc/rfc9112.html#section-5.2), and another line break ends the header (the next line is
    /// not a header of the response, and the browsers ignore it). A bare CR is a line break too.
    /// </summary>
    public static string GetFieldValueOnWire(string value)
    {
        if (value.AsSpan().IndexOfAny('\r', '\n') < 0)
            return value;

        var result = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var c = value[index];
            if (c is not ('\r' or '\n'))
            {
                result.Append(c);
                index++;
                continue;
            }

            var next = index + (c == '\r' && index + 1 < value.Length && value[index + 1] == '\n' ? 2 : 1);
            if (next >= value.Length || value[next] is not (' ' or '\t'))
                break;

            // obs-fold: the folding and the whitespace of the next line are one space
            while (result.Length > 0 && result[^1] is ' ' or '\t')
            {
                result.Length--;
            }

            result.Append(' ');
            index = next;
            while (index < value.Length && value[index] is ' ' or '\t')
            {
                index++;
            }
        }

        return result.ToString().TrimEnd(' ', '\t');
    }

    /// <summary>
    /// Parses a raw HTTP/1.1 response (the bytes a handler writes on the connection), or returns null when it is not a valid
    /// response (a network error). The start line and the header lines end with CRLF or a single LF
    /// (https://www.rfc-editor.org/rfc/rfc9112.html#section-2.2); a bare CR, a NUL byte or an invalid status line or field
    /// line make the message invalid. The header section also ends with the connection. The body is the rest of the bytes.
    /// </summary>
    public static Message? Parse(ReadOnlySpan<byte> bytes)
    {
        var lines = new List<string>();
        var index = 0;
        var headerSectionEnded = false;
        while (index < bytes.Length)
        {
            var end = bytes[index..].IndexOf((byte)'\n');
            var line = end < 0 ? bytes[index..] : bytes.Slice(index, end);
            index = end < 0 ? bytes.Length : index + end + 1;
            if (end >= 0 && line.Length > 0 && line[^1] == '\r')
            {
                line = line[..^1];
            }

            // A bare CR or a NUL byte before the body is invalid (https://www.rfc-editor.org/rfc/rfc9112.html#section-2.2)
            if (line.IndexOfAny((byte)'\r', (byte)'\0') >= 0)
                return null;

            if (line.Length == 0 && lines.Count > 0)
            {
                headerSectionEnded = true;
                break;
            }

            lines.Add(Encoding.Latin1.GetString(line));
        }

        if (lines.Count == 0 || ParseStatusLine(lines[0]) is not var (status, statusText))
            return null;

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            // https://www.rfc-editor.org/rfc/rfc9112.html#section-5.2: an obsolete line folding continues the previous value
            if (line[0] is ' ' or '\t')
            {
                if (headers.Count == 0)
                    return null;

                var previous = headers[^1];
                headers[^1] = new(previous.Key, (previous.Value + " " + line.Trim(' ', '\t')).Trim(' ', '\t'));
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || !IsToken(line.AsSpan(0, colon)))
                return null;

            headers.Add(new(line[..colon], line[(colon + 1)..].Trim(' ', '\t')));
        }

        return new Message(status, statusText, headers, headerSectionEnded ? bytes[index..].ToArray() : []);
    }

    /// <summary>
    /// Parses a status line (https://www.rfc-editor.org/rfc/rfc9112.html#section-4): <c>HTTP-version SP status-code [SP reason-phrase]</c>,
    /// where the status code is three digits.
    /// </summary>
    private static (int Status, string StatusText)? ParseStatusLine(string line)
    {
        if (line.Length < "HTTP/1.1 200".Length || !line.StartsWith("HTTP/", StringComparison.Ordinal) || !char.IsAsciiDigit(line[5]) || line[6] != '.' || !char.IsAsciiDigit(line[7]) || line[8] != ' ')
            return null;

        var code = line.AsSpan(9, 3);
        if (!char.IsAsciiDigit(code[0]) || !char.IsAsciiDigit(code[1]) || !char.IsAsciiDigit(code[2]))
            return null;

        var rest = line.AsSpan(12);
        if (rest.Length > 0 && rest[0] != ' ')
            return null;

        // reason-phrase = 1*( HTAB / SP / VCHAR / obs-text )
        var reason = rest.Length > 0 ? rest[1..] : [];
        foreach (var c in reason)
        {
            if (c is not ('\t' or ' ' or (>= '!' and <= '~') or >= '\x80'))
                return null;
        }

        return (int.Parse(code, NumberStyles.None, CultureInfo.InvariantCulture), reason.ToString());
    }

    private static bool IsToken(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the response of a handler as the network layer of a browser does: parses the raw message the handler wrote
    /// (<see cref="WptHandlerResponse.RawMessage"/>) and frames its body with its <c>Content-Length</c>, then decodes a body
    /// sent with a content coding (<see cref="WptHandlerResponse.BodyIsContentEncoded"/>).
    /// </summary>
    public static void Read(WptHandlerResponse response)
    {
        if (response.RawMessage is { } raw)
        {
            response.RawMessage = null;
            if (Parse(raw) is not { } message)
            {
                response.NetworkError = true;
                return;
            }

            response.Status = message.Status;
            response.StatusText = message.StatusText;
            response.Headers.Clear();
            response.Headers.AddRange(message.Headers);
            response.Body = message.Body;
            response.Streamed = true;
            response.Chunks.Clear();

            // The handler closes the connection: without Content-Length, the body is the rest of the bytes. Content-Length
            // headers whose values differ make the framing invalid; a value that is not a number is ignored, as the browsers do
            // (fetch/content-length/resources/content-lengths.json)
            switch (ExtractLength(message.Headers))
            {
                case -1:
                    response.NetworkError = true;
                    return;

                case -2:
                    break;

                case long length when length < message.Body.Length:
                    response.Body = message.Body[..(int)length];
                    break;

                case long length when length > message.Body.Length:
                    // The connection is closed before the end of the body
                    SetBodyError(response, message.Body);
                    return;
            }
        }

        if (response.BodyIsContentEncoded && response.Headers.Find(header => header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)) is { Key: not null } contentEncoding)
        {
            response.BodyIsContentEncoded = false;
            if (WptPipes.Decode(response.Body, contentEncoding.Value) is { } decoded)
            {
                response.Body = decoded;
            }
            else if (WptPipes.IsDecodedContentCoding(contentEncoding.Value))
            {
                // The headers are received, then the decoding of the body fails
                SetBodyError(response, []);
            }
        }
    }

    /// <summary>Makes the body of a response fail after <paramref name="received"/>: the response is received, and reading its body fails.</summary>
    private static void SetBodyError(WptHandlerResponse response, byte[] received)
    {
        response.Body = [];
        response.Chunks.Clear();
        response.Chunks.Add((TimeSpan.Zero, received));
        response.ChunksError = true;
        response.Streamed = true;
    }

    /// <summary>
    /// Extracts the length of a header list (https://fetch.spec.whatwg.org/#header-list-extract-a-length): null when there is
    /// no <c>Content-Length</c> header, -1 when its values differ, and -2 when the value is not a number (the two failures of
    /// the algorithm).
    /// </summary>
    public static long? ExtractLength(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        string? candidate = null;
        var found = false;
        foreach (var header in headers)
        {
            if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            found = true;
            foreach (var value in header.Value.Split(','))
            {
                var trimmed = value.Trim(' ', '\t');
                if (candidate is null)
                {
                    candidate = trimmed;
                }
                else if (candidate != trimmed)
                {
                    return -1;
                }
            }
        }

        if (!found)
            return null;

        return candidate is { Length: > 0 } && candidate.All(char.IsAsciiDigit) && long.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var length) ? length : -2;
    }
}
