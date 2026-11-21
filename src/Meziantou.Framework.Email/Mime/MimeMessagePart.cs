namespace Meziantou.Framework.Email;

/// <summary>An entity that contains a message (<c>message/rfc822</c>), such as a forwarded message.</summary>
public sealed class MimeMessagePart : MimeEntity
{
    /// <summary>Initializes a new instance of the <see cref="MimeMessagePart"/> class.</summary>
    /// <param name="message">The message.</param>
    public MimeMessagePart(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
        ContentType = new MimeContentType("message", "rfc822");
    }

    internal MimeMessagePart(MimeMessage message, bool parsed)
    {
        _ = parsed;
        Message = message;
    }

    /// <summary>Gets or sets the message.</summary>
    public MimeMessage Message { get; set; }

    internal override void WriteTo(Stream stream)
    {
        // The message is written as it is: a transfer encoding of the original part does not apply to it anymore
        Headers.RemoveAll("Content-Transfer-Encoding");
        Internals.MimeWriter.WriteHeaders(stream, Headers);
        Message.WriteTo(stream);
    }
}
