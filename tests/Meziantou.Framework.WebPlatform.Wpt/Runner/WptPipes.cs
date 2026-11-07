using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>A pipe of the WPT server: its name and its arguments (https://web-platform-tests.org/writing-tests/server-pipes.html).</summary>
internal sealed record WptPipe(string Name, IReadOnlyList<string> Arguments);

/// <summary>
/// The pipes of the WPT server, which transform the responses of the files and of the Python handlers: the <c>.sub.</c>
/// files and the <c>pipe</c> query parameter (https://web-platform-tests.org/writing-tests/server-pipes.html). The runner
/// applies <c>status()</c>, <c>header()</c>, <c>trickle()</c> (its delays), <c>gzip</c> (its headers) and <c>sub</c>.
/// </summary>
internal static class WptPipes
{
    /// <summary>
    /// Returns the pipes of a response (<c>wrap_pipeline</c> of wptserve): <c>sub</c> for a <c>.sub.</c> file (<c>sub(html)</c>
    /// for a markup file, whose substituted values are HTML-escaped, <c>sub(none)</c> otherwise), then the pipes of the last
    /// <c>pipe</c> query parameter.
    /// </summary>
    public static List<WptPipe> Get(Url url)
    {
        var pipes = new List<WptPipe>();
        if (url.Pathname.Contains(".sub.", StringComparison.Ordinal))
        {
            var isMarkup = Path.GetExtension(url.Pathname).ToLowerInvariant() is ".html" or ".htm" or ".xht" or ".xhtml" or ".xml" or ".svg";
            pipes.Add(new WptPipe("sub", [isMarkup ? "html" : "none"]));
        }

        if (url.SearchParams.GetAll("pipe") is [.., var pipe])
        {
            pipes.AddRange(Parse(pipe));
        }

        return pipes;
    }

    /// <summary>Parses a pipe string, e.g. <c>status(404)|header(X-Test,a\,b)</c> (the <c>PipeTokenizer</c> of wptserve).</summary>
    public static List<WptPipe> Parse(string value)
    {
        var pipes = new List<WptPipe>();
        var index = 0;
        while (index < value.Length)
        {
            // The name of the function, ended by '(' (arguments), '|' (next function) or the end of the string
            var name = new StringBuilder();
            while (index < value.Length && value[index] is not ('(' or '|'))
            {
                name.Append(value[index++]);
            }

            var arguments = new List<string>();
            if (index < value.Length && value[index] == '(')
            {
                index++;
                var argument = new StringBuilder();
                while (true)
                {
                    if (index >= value.Length)
                    {
                        arguments.Add(argument.ToString());
                        break;
                    }

                    var c = value[index++];
                    if (c == '\\' && index < value.Length)
                    {
                        var escaped = value[index++];
                        argument.Append(escaped switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => escaped });
                    }
                    else if (c == ',')
                    {
                        arguments.Add(argument.ToString());
                        argument.Clear();
                    }
                    else if (c == ')')
                    {
                        arguments.Add(argument.ToString());
                        break;
                    }
                    else
                    {
                        argument.Append(c);
                    }
                }
            }
            else if (index < value.Length)
            {
                // '|'
                index++;
            }

            if (name.Length > 0)
            {
                pipes.Add(new WptPipe(name.ToString(), arguments));
            }
        }

        return pipes;
    }

    /// <summary>Whether the <c>sub</c> pipe applies to the response.</summary>
    public static bool HasSubstitutions(List<WptPipe> pipes) => pipes.Exists(pipe => pipe.Name == "sub");

    /// <summary>The status set by the last <c>status(code)</c> pipe, or null.</summary>
    public static int? GetStatus(List<WptPipe> pipes)
    {
        int? status = null;
        foreach (var pipe in pipes)
        {
            if (pipe.Name == "status" && pipe.Arguments is [var code, ..] && int.TryParse(code, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var value))
            {
                status = value;
            }
        }

        return status;
    }

    /// <summary>
    /// Applies the <c>header(name, value[, append])</c> pipes to the headers of a response: the header replaces the headers
    /// with the same name, unless <c>append</c> is <c>True</c>. The values are trimmed of the HTTP whitespace (spaces and tabs),
    /// as the HTTP parser of a browser does: the other control characters are kept.
    /// </summary>
    public static void ApplyHeaders(List<KeyValuePair<string, string>> headers, List<WptPipe> pipes)
    {
        foreach (var pipe in pipes)
        {
            if (pipe.Name != "header" || pipe.Arguments.Count < 2)
                continue;

            var name = pipe.Arguments[0].Trim();
            var value = pipe.Arguments[1].Trim(' ', '\t');

            // wptserve writes the value as it is: a line break ends the header, and the browsers ignore the next line
            if (value.IndexOfAny(['\r', '\n']) is var lineBreak and >= 0)
            {
                value = value[..lineBreak];
            }

            var append = pipe.Arguments.Count > 2 && pipe.Arguments[2].Trim() is "True" or "true" or "1";
            if (!append)
            {
                headers.RemoveAll(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            headers.Add(new(name, value));
        }
    }

    /// <summary>
    /// Applies the <c>gzip</c> pipe to the headers of a response: the <c>Content-Encoding</c> is <c>gzip</c> and the
    /// <c>Content-Length</c> is the length of the compressed body. The body is not compressed: the network layer of a browser
    /// decodes it.
    /// </summary>
    public static void ApplyGzip(List<KeyValuePair<string, string>> headers, List<WptPipe> pipes, ReadOnlySpan<byte> body)
    {
        if (!pipes.Exists(pipe => pipe.Name == "gzip"))
            return;

        headers.RemoveAll(header => header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase));
        headers.Add(new("Content-Encoding", "gzip"));
        headers.Add(new("Content-Length", GetGzipLength(body).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Decodes a body whose content coding is <c>gzip</c>, <c>deflate</c> or <c>br</c>, or returns null (another coding, or invalid data).</summary>
    public static byte[]? Decode(ReadOnlySpan<byte> body, string contentEncoding)
    {
        using var input = new MemoryStream(body.ToArray());
        using Stream? stream = contentEncoding.Trim().ToLowerInvariant() switch
        {
            "gzip" => new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress),
            "deflate" => new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress),
            "br" => new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress),
            _ => null,
        };
        if (stream is null)
            return null;

        try
        {
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>The length of a body compressed with gzip (the default compression level, as <c>gzip.GzipFile</c> in Python).</summary>
    public static long GetGzipLength(ReadOnlySpan<byte> body)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(body);
        }

        return compressed.Length;
    }

    /// <summary>
    /// The parts of a body sent by the first <c>trickle()</c> pipe (https://web-platform-tests.org/writing-tests/server-pipes.html#trickle),
    /// as wptserve sends them, or null when there is none: the bytes sent with the headers, then the chunks sent after
    /// their delays (<c>d&lt;seconds&gt;</c>). A number of bytes sends the next bytes, the rest of the body is sent after
    /// the last part, and a final <c>r&lt;n&gt;</c> repeats the <c>n</c> previous parts until the whole body is sent. A
    /// delay after the last bytes is an empty chunk (the end of the body).
    /// </summary>
    public static (byte[] First, List<(TimeSpan Delay, byte[] Data)> Chunks)? GetTrickleChunks(byte[] content, List<WptPipe> pipes)
    {
        if (pipes.FirstOrDefault(pipe => pipe.Name == "trickle") is not { Arguments: [var parts, ..] })
            return null;

        // The consecutive parts of the same type are merged, as wptserve does
        var items = new List<(char Type, double Value)>();
        foreach (var part in parts.Split(':'))
        {
            var item = part.StartsWith('d', StringComparison.Ordinal) ? ('d', double.Parse(part.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture))
                : part.StartsWith('r', StringComparison.Ordinal) ? ('r', int.Parse(part.AsSpan(1), CultureInfo.InvariantCulture))
                : ('b', int.Parse(part, CultureInfo.InvariantCulture));
            if (items.Count > 0 && items[^1].Type == item.Item1)
            {
                items[^1] = (item.Item1, items[^1].Value + item.Item2);
            }
            else
            {
                items.Add(item);
            }
        }

        var segments = new List<(TimeSpan Delay, byte[] Data)>();
        var delay = TimeSpan.Zero;
        var offset = 0;
        Send(items, repeat: false);
        if (delay > TimeSpan.Zero)
        {
            segments.Add((delay, []));
        }

        var first = segments.TakeWhile(segment => segment.Delay == TimeSpan.Zero).SelectMany(segment => segment.Data).ToArray();
        return (first, [.. segments.SkipWhile(segment => segment.Delay == TimeSpan.Zero)]);

        void Send(List<(char Type, double Value)> items, bool repeat)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var (type, value) = items[i];
                switch (type)
                {
                    case 'b':
                        var count = Math.Min((int)value, content.Length - offset);
                        Add(content[offset..(offset + count)]);
                        offset += count;
                        break;

                    case 'd':
                        delay += TimeSpan.FromSeconds(value);
                        break;

                    // Only a final repeat is applied (it repeats the parts before it, which must send bytes)
                    case 'r' when i == items.Count - 1 && (int)value < items.Count:
                        var repeated = items.GetRange(items.Count - 1 - (int)value, (int)value);
                        while (offset < content.Length && repeated.Exists(item => item is ('b', > 0)))
                        {
                            Send(repeated, repeat: true);
                        }

                        break;
                }
            }

            if (!repeat && offset < content.Length)
            {
                Add(content[offset..]);
                offset = content.Length;
            }
        }

        void Add(byte[] data)
        {
            if (data.Length == 0 && delay == TimeSpan.Zero)
                return;

            segments.Add((delay, data));
            delay = TimeSpan.Zero;
        }
    }

    /// <summary>
    /// The total delay of the <c>trickle()</c> pipes (the <c>d&lt;seconds&gt;</c> parts), or null when there is none: the
    /// runner sends the response at once, after the delays.
    /// </summary>
    public static TimeSpan? GetTrickleDelay(List<WptPipe> pipes)
    {
        TimeSpan? delay = null;
        foreach (var pipe in pipes)
        {
            if (pipe.Name != "trickle" || pipe.Arguments is not [var parts, ..])
                continue;

            delay ??= TimeSpan.Zero;
            foreach (var part in parts.Split(':'))
            {
                if (part.StartsWith('d', StringComparison.Ordinal) && double.TryParse(part.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    delay += TimeSpan.FromSeconds(seconds);
                }
            }
        }

        return delay;
    }
}
