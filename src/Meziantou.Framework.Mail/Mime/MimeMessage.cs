using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>A message in the Internet Message Format ([RFC 5322]) with its MIME structure ([RFC 2045]): the content of an <c>.eml</c> file.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Mail.Tests/MimeMessageTests.cs" ref:region="MimeMessage_Load" ref:dedent="true">
/// var message = MimeMessage.Load(path);
/// Assert.Equal("Attachments", message.Subject);
/// Assert.Equal("alice@example.com", message.From[0].Address);
/// Assert.Equal("See the attachments.", message.TextBody?.TrimEnd());
///
/// foreach (var attachment in message.Attachments.OfType&lt;MimePart&gt;())
/// {
///     // The file name is decoded (RFC 2047 and RFC 2231), and the content is decoded from base64 or quoted-printable
///     var fileName = attachment.FileName;
///     var content = attachment.GetContent();
///     Assert.NotNull(content);
///     _ = fileName;
/// }
/// </code>
/// </example>
public sealed class MimeMessage
{
    private const string MimeVersion = "MIME-Version";

    private readonly bool _parsed;

    /// <summary>Initializes a new instance of the <see cref="MimeMessage"/> class with an empty text body.</summary>
    public MimeMessage()
    {
        var body = new MimePart();
        body.SetText("");
        Body = body;
    }

    internal MimeMessage(bool parsed)
    {
        _parsed = parsed;
        Body = new MimePart();
    }

    /// <summary>Gets the headers of the message, except the Content-* headers, which are the headers of <see cref="Body"/>.</summary>
    public MimeHeaderCollection Headers { get; } = new();

    /// <summary>Gets or sets the body: a text part, a multipart, or any other entity.</summary>
    public MimeEntity Body { get; set; }

    /// <summary>Gets or sets the subject.</summary>
    public string? Subject
    {
        get => Headers["Subject"];
        set => Headers.Set("Subject", value);
    }

    /// <summary>Gets or sets the authors of the message.</summary>
    public IReadOnlyList<MailboxAddress> From
    {
        get => GetAddresses("From");
        set => SetAddresses("From", value);
    }

    /// <summary>Gets or sets the sender, when it is not the author.</summary>
    public MailboxAddress? Sender
    {
        get => MailboxAddress.Parse(Headers.Find("Sender")?.RawValue);
        set => SetAddresses("Sender", value is null ? null : [value]);
    }

    /// <summary>Gets or sets the addresses replies go to.</summary>
    public IReadOnlyList<MailboxAddress> ReplyTo
    {
        get => GetAddresses("Reply-To");
        set => SetAddresses("Reply-To", value);
    }

    /// <summary>Gets or sets the primary recipients.</summary>
    public IReadOnlyList<MailboxAddress> To
    {
        get => GetAddresses("To");
        set => SetAddresses("To", value);
    }

    /// <summary>Gets or sets the carbon copy recipients.</summary>
    public IReadOnlyList<MailboxAddress> Cc
    {
        get => GetAddresses("Cc");
        set => SetAddresses("Cc", value);
    }

    /// <summary>Gets or sets the blind carbon copy recipients.</summary>
    public IReadOnlyList<MailboxAddress> Bcc
    {
        get => GetAddresses("Bcc");
        set => SetAddresses("Bcc", value);
    }

    /// <summary>Gets or sets the date of the message, or <see langword="null"/> when it has none or it is invalid.</summary>
    public DateTimeOffset? Date
    {
        get => MimeDate.Parse(Headers.Find("Date")?.RawValue);
        set => Headers.Set("Date", value is null ? null : MimeHeader.CreateRaw("Date", MimeDate.Format(value.Value)));
    }

    /// <summary>Gets or sets the identifier of the message, without its angle brackets.</summary>
    public string? MessageId
    {
        get => ParseIds(Headers.Find("Message-ID")?.RawValue) is [var id, ..] ? id : null;
        set => Headers.Set("Message-ID", FormatIds("Message-ID", value is null ? [] : [value]));
    }

    /// <summary>Gets or sets the identifier of the message this one replies to, without its angle brackets.</summary>
    public string? InReplyTo
    {
        get => ParseIds(Headers.Find("In-Reply-To")?.RawValue) is [var id, ..] ? id : null;
        set => Headers.Set("In-Reply-To", FormatIds("In-Reply-To", value is null ? [] : [value]));
    }

    /// <summary>Gets or sets the identifiers of the messages of the thread, without their angle brackets.</summary>
    public IReadOnlyList<string> References
    {
        get => ParseIds(Headers.Find("References")?.RawValue);
        set => Headers.Set("References", FormatIds("References", value ?? []));
    }

    /// <summary>Gets the plain text body: the first <c>text/plain</c> part that is not an attachment.</summary>
    public string? TextBody => FindBody("plain")?.GetText();

    /// <summary>Gets the HTML body: the first <c>text/html</c> part that is not an attachment.</summary>
    public string? HtmlBody => FindBody("html")?.GetText();

    /// <summary>Gets the attachments: the leaf parts and the embedded messages, except the text and HTML bodies.</summary>
    public IEnumerable<MimeEntity> Attachments
    {
        get
        {
            var text = FindBody("plain");
            var html = FindBody("html");
            foreach (var entity in EnumerateLeaves(Body, depth: 0))
            {
                if (entity != text && entity != html)
                {
                    yield return entity;
                }
            }
        }
    }

    /// <summary>Reads a message.</summary>
    /// <param name="data">The content of the message.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The message.</returns>
    /// <exception cref="MailResourceLimitException">The message exceeds a limit of <paramref name="options"/>.</exception>
    public static MimeMessage Load(ReadOnlyMemory<byte> data, MimeReadOptions? options = null) => MimeParser.ParseMessage(data, options ?? MimeReadOptions.Default, depth: 0);

    /// <summary>Reads a message from a stream, until its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The message.</returns>
    /// <exception cref="MailResourceLimitException">The message is larger than <see cref="MimeReadOptions.MaxSize"/>.</exception>
    public static MimeMessage Load(Stream stream, MimeReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= MimeReadOptions.Default;
        return Load(StreamHelper.ReadAll(stream, options.MaxSize), options);
    }

    /// <summary>Reads a message from a file, such as an <c>.eml</c> file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The message.</returns>
    public static MimeMessage Load(string path, MimeReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Load(stream, options);
    }

    /// <summary>Writes the message. The parts read from a message and not modified are written as they were read.</summary>
    /// <param name="stream">The stream.</param>
    public void WriteTo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!_parsed && Headers.Find(MimeVersion) is null && Body.Headers.Count > 0)
        {
            Headers.Add(MimeHeader.CreateRaw(MimeVersion, "1.0"));
        }

        MimeWriter.WriteHeaders(stream, Headers, endOfHeaders: false);
        Body.WriteTo(stream);
    }

    /// <summary>Writes the message to a file.</summary>
    /// <param name="path">The path of the file.</param>
    public void WriteTo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.Create(path);
        WriteTo(stream);
    }

    /// <summary>Returns the bytes of the message.</summary>
    /// <returns>The bytes of the message.</returns>
    public byte[] ToByteArray()
    {
        using var stream = new MemoryStream();
        WriteTo(stream);
        return stream.ToArray();
    }

    internal static IEnumerable<MimeEntity> EnumerateLeaves(MimeEntity entity, int depth)
    {
        if (entity is MimeMultipart multipart && depth < 128)
        {
            foreach (var part in multipart.Parts)
            {
                foreach (var leaf in EnumerateLeaves(part, depth + 1))
                {
                    yield return leaf;
                }
            }
        }
        else
        {
            yield return entity;
        }
    }

    private MimePart? FindBody(string subtype)
    {
        foreach (var entity in EnumerateLeaves(Body, depth: 0))
        {
            if (entity is MimePart part && part.ContentType.IsMimeType("text", subtype) && !part.IsAttachment)
            {
                return part;
            }
        }

        return null;
    }

    private List<MailboxAddress> GetAddresses(string name)
    {
        var result = new List<MailboxAddress>();
        foreach (var header in Headers.FindAll(name))
        {
            result.AddRange(MailboxAddress.ParseList(header.RawValue));
        }

        return result;
    }

    private void SetAddresses(string name, IEnumerable<MailboxAddress>? addresses)
    {
        var list = addresses?.Where(address => address.CanBeWritten).ToList();
        Headers.Set(name, list is null or { Count: 0 } ? null : MimeHeader.CreateRaw(name, MailboxAddress.FormatList(list)));
    }

    /// <summary>Formats identifiers between angle brackets; the characters an identifier cannot contain (angle brackets, whitespace) are removed.</summary>
    private static MimeHeader? FormatIds(string name, IEnumerable<string> ids)
    {
        var values = ids.Select(id => string.Concat(id.Where(c => c is not ('<' or '>') && !char.IsWhiteSpace(c) && !char.IsControl(c)))).Where(id => id.Length > 0).ToList();
        return values.Count == 0 ? null : MimeHeader.CreateRaw(name, string.Join(' ', values.Select(id => "<" + id + ">")));
    }

    private static List<string> ParseIds(string? value)
    {
        var result = new List<string>();
        if (value is null)
        {
            return result;
        }

        var position = 0;
        while (position < value.Length)
        {
            var start = value.IndexOf("<", position, StringComparison.Ordinal);
            if (start < 0)
            {
                // An identifier without angle brackets
                var bare = value[position..].Trim();
                if (bare.Length > 0 && result.Count == 0 && !bare.Contains(' ', StringComparison.Ordinal))
                {
                    result.Add(bare);
                }

                break;
            }

            var end = value.IndexOf(">", start, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            var id = value[(start + 1)..end].Trim();
            if (id.Length > 0)
            {
                result.Add(id);
            }

            position = end + 1;
        }

        return result;
    }
}
