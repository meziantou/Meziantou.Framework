namespace Meziantou.Framework.Email.Storage;

/// <summary>A message of an mbox file, with the <c>From </c> line that starts it.</summary>
public sealed class MboxMessage
{
    internal MboxMessage(string fromLine, long offset, ReadOnlyMemory<byte> content, MimeReadOptions options)
    {
        FromLine = fromLine;
        Offset = offset;
        Content = content;
        _options = options;
        (Sender, Date) = MboxFromLine.Parse(fromLine);
    }

    private readonly MimeReadOptions _options;
    private MimeMessage? _message;

    /// <summary>Gets the <c>From </c> line that starts the message, without its line break.</summary>
    public string FromLine { get; }

    /// <summary>Gets the address of the envelope sender, the first word of the <c>From </c> line.</summary>
    public string? Sender { get; }

    /// <summary>Gets the date of the <c>From </c> line, when it has one in the format of <c>asctime</c> (<c>Mon Jan  5 09:00:00 2026</c>), in UTC.</summary>
    public DateTimeOffset? Date { get; }

    /// <summary>Gets the position of the <c>From </c> line in the file.</summary>
    public long Offset { get; }

    /// <summary>Gets the bytes of the message, unescaped, without the <c>From </c> line.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>Gets the message, read when it is first accessed.</summary>
    /// <exception cref="EmailResourceLimitException">The message exceeds a limit of <see cref="MboxReadOptions.MessageOptions"/>.</exception>
    public MimeMessage Message => _message ??= MimeMessage.Load(Content, _options);

    /// <summary>Returns the <c>From </c> line.</summary>
    /// <returns>The <c>From </c> line.</returns>
    public override string ToString() => FromLine;
}
