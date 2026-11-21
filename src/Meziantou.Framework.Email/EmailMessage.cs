using System.Text;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email;

/// <summary>
/// An email, independent of the format it is stored in: the model the formats of the library convert to and from, such as
/// an <c>.eml</c> file (<see cref="MimeMessage"/>) or an item of an Outlook data file (<see cref="MapiMessage"/>).
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Email.Tests/EmailMessageTests.cs" ref:region="EmailMessage_Convert" ref:dedent="true">
/// // An item of an Outlook data file, saved as an .eml file
/// using (var file = PstFile.Open(pstPath))
/// {
///     var item = file.GetFolder("Inbox")!.EnumerateItems().First();
///     EmailMessage.FromMapi(item).ToMime().WriteTo(emlPath);
/// }
///
/// // The .eml file, read back
/// var email = EmailMessage.FromMime(MimeMessage.Load(emlPath));
/// Assert.Equal("Welcome to Microsoft Office Outlook 2003", email.Subject);
/// Assert.HasCount(11, email.Attachments);
/// </code>
/// </example>
public sealed class EmailMessage
{
    // The headers the model represents with its own members
    private static readonly HashSet<string> MappedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "From", "Sender", "Reply-To", "To", "Cc", "Bcc", "Subject", "Date", "Message-ID", "In-Reply-To", "References", "MIME-Version", "Importance", "X-Priority", "Keywords",
    };

    /// <summary>Gets or sets the subject.</summary>
    public string? Subject { get; set; }

    /// <summary>Gets or sets the author.</summary>
    public MailboxAddress? From { get; set; }

    /// <summary>Gets or sets the sender, when it is not the author.</summary>
    public MailboxAddress? Sender { get; set; }

    /// <summary>Gets the primary recipients.</summary>
    public IList<MailboxAddress> To { get; } = [];

    /// <summary>Gets the carbon copy recipients.</summary>
    public IList<MailboxAddress> Cc { get; } = [];

    /// <summary>Gets the blind carbon copy recipients.</summary>
    public IList<MailboxAddress> Bcc { get; } = [];

    /// <summary>Gets the addresses replies go to.</summary>
    public IList<MailboxAddress> ReplyTo { get; } = [];

    /// <summary>Gets or sets the date the message was sent.</summary>
    public DateTimeOffset? Date { get; set; }

    /// <summary>Gets or sets the identifier of the message, without angle brackets.</summary>
    public string? MessageId { get; set; }

    /// <summary>Gets or sets the identifier of the message this one replies to, without angle brackets.</summary>
    public string? InReplyTo { get; set; }

    /// <summary>Gets the identifiers of the messages of the thread, without angle brackets.</summary>
    public IList<string> References { get; } = [];

    /// <summary>Gets or sets the importance.</summary>
    public EmailImportance Importance { get; set; } = EmailImportance.Normal;

    /// <summary>Gets the categories (keywords).</summary>
    public IList<string> Categories { get; } = [];

    /// <summary>Gets or sets the plain text body.</summary>
    public string? TextBody { get; set; }

    /// <summary>Gets or sets the HTML body.</summary>
    public string? HtmlBody { get; set; }

    /// <summary>Gets or sets the RTF body, which Outlook formats store; a MIME message does not have one.</summary>
    public string? RtfBody { get; set; }

    /// <summary>Gets the attachments.</summary>
    public IList<EmailAttachment> Attachments { get; } = [];

    /// <summary>Gets the other headers, such as Received or List-Id, with their decoded values.</summary>
    public IList<KeyValuePair<string, string>> Headers { get; } = [];

    /// <summary>Creates the model of a MIME message. A <c>winmail.dat</c> attachment (<c>application/ms-tnef</c>) is replaced by the attachments of the Outlook item it contains, and gives the bodies the message does not have; a malformed one is kept as an attachment.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The model.</returns>
    public static EmailMessage FromMime(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return FromMime(message, depth: 0);
    }

    private static bool IsTnef(MimePart part)
    {
        var type = part.ContentType;
        return type.MediaType.Equals("application", StringComparison.OrdinalIgnoreCase)
            && (type.MediaSubtype.Equals("ms-tnef", StringComparison.OrdinalIgnoreCase) || type.MediaSubtype.Equals("vnd.ms-tnef", StringComparison.OrdinalIgnoreCase));
    }

    private static EmailMessage? TryReadTnef(byte[] content)
    {
        if (!TnefFile.HasSignature(content))
        {
            return null;
        }

        try
        {
            return FromMapi(TnefFile.Load(content));
        }
        catch (EmailException)
        {
            // A malformed stream is kept as an attachment
            return null;
        }
    }

    private static EmailMessage FromMime(MimeMessage message, int depth)
    {
        var result = new EmailMessage
        {
            Subject = message.Subject,
            From = message.From is [var from, ..] ? from : null,
            Sender = message.Sender,
            Date = message.Date,
            MessageId = message.MessageId,
            InReplyTo = message.InReplyTo,
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
            Importance = ParseImportance(message.Headers["Importance"], message.Headers["X-Priority"]),
        };

        AddRange(result.To, message.To);
        AddRange(result.Cc, message.Cc);
        AddRange(result.Bcc, message.Bcc);
        AddRange(result.ReplyTo, message.ReplyTo);
        AddRange(result.References, message.References);
        if (message.Headers["Keywords"] is { } keywords)
        {
            AddRange(result.Categories, keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        foreach (var header in message.Headers)
        {
            if (!MappedHeaders.Contains(header.Name))
            {
                result.Headers.Add(new(header.Name, header.Value));
            }
        }

        foreach (var entity in message.Attachments)
        {
            var disposition = entity.ContentDisposition;
            var attachment = new EmailAttachment
            {
                ContentType = entity.ContentType.MimeType.ToLowerInvariant(),
                ContentId = entity.ContentId,
                IsInline = disposition is null ? entity.ContentId is not null : !disposition.IsAttachment,
                FileName = disposition?.FileName ?? entity.ContentType.Name,
            };

            switch (entity)
            {
                case MimePart part when IsTnef(part) && TryReadTnef(part.GetContent()) is { } tnef:
                    // A winmail.dat attachment: the bodies and the attachments of the Outlook item it contains
                    result.TextBody ??= tnef.TextBody;
                    result.HtmlBody ??= tnef.HtmlBody;
                    result.RtfBody ??= tnef.RtfBody;
                    foreach (var item in tnef.Attachments)
                    {
                        result.Attachments.Add(item);
                    }

                    continue;
                case MimePart part:
                    attachment.Content = part.GetContent();
                    break;
                case MimeMessagePart messagePart when depth < 32:
                    attachment.EmbeddedMessage = FromMime(messagePart.Message, depth + 1);
                    break;
            }

            result.Attachments.Add(attachment);
        }

        return result;
    }

    /// <summary>Creates a MIME message: a text and an HTML alternative, the inline attachments related to the HTML body, and the other attachments.</summary>
    /// <returns>The MIME message.</returns>
    public MimeMessage ToMime() => ToMime(depth: 0);

    private MimeMessage ToMime(int depth)
    {
        var message = new MimeMessage();
        foreach (var (name, value) in Headers)
        {
            message.Headers.Add(name, value);
        }

        message.From = From is null ? [] : [From];
        message.Sender = Sender;
        message.To = [.. To];
        message.Cc = [.. Cc];
        message.Bcc = [.. Bcc];
        message.ReplyTo = [.. ReplyTo];
        message.Subject = Subject;
        message.Date = Date;
        message.MessageId = MessageId;
        message.InReplyTo = InReplyTo;
        message.References = [.. References];
        if (Categories.Count > 0)
        {
            message.Headers.Set("Keywords", string.Join(", ", Categories));
        }

        if (Importance != EmailImportance.Normal)
        {
            message.Headers.Set("Importance", Importance == EmailImportance.High ? "high" : "low");
        }

        // multipart/mixed( multipart/related( multipart/alternative(text, html), inline... ), attachments... )
        MimeEntity? body = null;
        if (TextBody is not null)
        {
            body = CreateTextPart(TextBody, "plain");
        }

        if (HtmlBody is not null)
        {
            MimeEntity html = CreateTextPart(HtmlBody, "html");
            var inline = Attachments.Where(attachment => attachment.IsInline && attachment.ContentId is not null && attachment.EmbeddedMessage is null).ToList();
            if (inline.Count > 0)
            {
                var related = new MimeMultipart("related");
                related.Parts.Add(html);
                foreach (var attachment in inline)
                {
                    related.Parts.Add(CreateAttachmentPart(attachment, depth));
                }

                html = related;
            }

            if (body is null)
            {
                body = html;
            }
            else
            {
                var alternative = new MimeMultipart("alternative");
                alternative.Parts.Add(body);
                alternative.Parts.Add(html);
                body = alternative;
            }
        }

        var others = Attachments.Where(attachment => !(HtmlBody is not null && attachment.IsInline && attachment.ContentId is not null && attachment.EmbeddedMessage is null)).ToList();
        if (others.Count > 0)
        {
            var mixed = new MimeMultipart("mixed");
            if (body is not null)
            {
                mixed.Parts.Add(body);
            }

            foreach (var attachment in others)
            {
                mixed.Parts.Add(CreateAttachmentPart(attachment, depth));
            }

            body = mixed;
        }

        message.Body = body ?? CreateTextPart("", "plain");
        return message;
    }

    /// <summary>Creates the model of an item of an Outlook data file.</summary>
    /// <param name="message">The item.</param>
    /// <returns>The model.</returns>
    /// <exception cref="EmailException">The item is malformed.</exception>
    public static EmailMessage FromMapi(MapiMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var address = message.SenderSmtpAddress ?? message.SenderEmailAddress;
        var result = new EmailMessage
        {
            Subject = message.Subject,
            From = address is null && message.SenderName is null ? null : new MailboxAddress(message.SenderName, address ?? ""),
            Date = (message.ClientSubmitTime ?? message.MessageDeliveryTime) is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null,
            MessageId = TrimId(message.InternetMessageId),
            InReplyTo = TrimId(message.InReplyToId),
            TextBody = message.Body,
            HtmlBody = message.BodyHtml,
            RtfBody = message.GetRtfBody() is { } rtf ? Encoding.Latin1.GetString(rtf) : null,
            Importance = (EmailImportance)message.Importance,
        };

        foreach (var recipient in message.Recipients)
        {
            var recipientAddress = recipient.SmtpAddress ?? recipient.EmailAddress ?? "";
            var mailbox = new MailboxAddress(string.Equals(recipient.DisplayName, recipientAddress, StringComparison.OrdinalIgnoreCase) ? null : recipient.DisplayName, recipientAddress);
            var list = recipient.RecipientType switch
            {
                MapiRecipientType.Cc => result.Cc,
                MapiRecipientType.Bcc => result.Bcc,
                MapiRecipientType.Originator => null,
                _ => result.To,
            };

            list?.Add(mailbox);
        }

        if (message.InternetReferences is { } references)
        {
            foreach (var reference in references.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                result.References.Add(TrimId(reference)!);
            }
        }

        AddRange(result.Categories, message.Categories);
        if (message.TransportMessageHeaders is { } transportHeaders)
        {
            var position = 0;
            foreach (var header in MimeParser.ReadHeaders(Encoding.UTF8.GetBytes(transportHeaders + "\r\n\r\n"), ref position, allowMboxFromLine: false))
            {
                if (!MappedHeaders.Contains(header.Name) && !header.Name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    result.Headers.Add(new(header.Name, header.Value));
                }
            }
        }

        foreach (var attachment in message.Attachments)
        {
            var embedded = attachment.GetEmbeddedMessage();
            result.Attachments.Add(new EmailAttachment
            {
                FileName = embedded is null ? attachment.Name : attachment.LongFileName ?? attachment.FileName,
                ContentType = attachment.MimeType ?? (embedded is null ? "application/octet-stream" : "message/rfc822"),
                ContentId = attachment.ContentId,
                IsInline = attachment.IsHidden,
                Content = embedded is null ? attachment.GetContent() : default,
                EmbeddedMessage = embedded is null ? null : FromMapi(embedded),
            });
        }

        return result;
    }

    /// <summary>Creates a builder that writes the message as an item of an Outlook data file.</summary>
    /// <returns>The builder, to add to a folder of a <see cref="PstWriter"/>.</returns>
    public MapiMessageBuilder ToMapiBuilder()
    {
        var builder = new MapiMessageBuilder
        {
            Subject = Subject,
            SenderName = From?.Name,
            SenderEmailAddress = From?.Address,
            SenderAddressType = From is null ? null : "SMTP",
            ClientSubmitTime = Date?.UtcDateTime,
            MessageDeliveryTime = Date?.UtcDateTime,
            InternetMessageId = MessageId is null ? null : "<" + MessageId + ">",
            InReplyToId = InReplyTo is null ? null : "<" + InReplyTo + ">",
            InternetReferences = References.Count == 0 ? null : string.Join(' ', References.Select(id => "<" + id + ">")),
            Body = TextBody,
            BodyHtml = HtmlBody,
            Importance = (MapiImportance)Importance,
            Categories = Categories.Count == 0 ? null : [.. Categories],
            TransportMessageHeaders = Headers.Count == 0 ? null : string.Concat(Headers.Select(header => header.Key + ": " + header.Value + "\r\n")),
        };

        if (RtfBody is not null)
        {
            builder.SetRtfBody(Encoding.Latin1.GetBytes(RtfBody));
        }

        foreach (var (list, type) in new[] { (To, MapiRecipientType.To), (Cc, MapiRecipientType.Cc), (Bcc, MapiRecipientType.Bcc) })
        {
            foreach (var address in list)
            {
                builder.Recipients.Add(new MapiRecipientBuilder(address.Name, address.Address, type));
            }
        }

        foreach (var attachment in Attachments)
        {
            var item = attachment.EmbeddedMessage is { } embedded
                ? MapiAttachmentBuilder.FromMessage(embedded.ToMapiBuilder())
                : MapiAttachmentBuilder.FromContent(attachment.FileName ?? "attachment", attachment.Content.ToArray(), attachment.ContentType);
            if (attachment.EmbeddedMessage is not null)
            {
                item.LongFileName = attachment.FileName;
            }

            item.ContentId = attachment.ContentId;
            item.IsHidden = attachment.IsInline;
            builder.Attachments.Add(item);
        }

        return builder;
    }

    private static MimePart CreateTextPart(string text, string subtype)
    {
        var part = new MimePart();
        part.SetText(text, subtype);
        return part;
    }

    private static MimeEntity CreateAttachmentPart(EmailAttachment attachment, int depth)
    {
        MimeEntity entity;
        if (attachment.EmbeddedMessage is { } embedded && depth < 32)
        {
            entity = new MimeMessagePart(embedded.ToMime(depth + 1));
        }
        else
        {
            var slash = attachment.ContentType.IndexOf('/', StringComparison.Ordinal);
            var contentType = slash > 0 ? new MimeContentType(attachment.ContentType[..slash], attachment.ContentType[(slash + 1)..]) : new MimeContentType("application", "octet-stream");
            if (attachment.FileName is not null)
            {
                contentType.Name = attachment.FileName;
            }

            var part = new MimePart { ContentType = contentType };
            part.SetContent(attachment.Content.ToArray());
            part.ContentTransferEncoding = "base64";
            entity = part;
        }

        entity.ContentDisposition = new MimeContentDisposition(attachment.IsInline ? MimeContentDisposition.Inline : MimeContentDisposition.Attachment) { FileName = attachment.FileName };
        entity.ContentId = attachment.ContentId;
        return entity;
    }

    private static EmailImportance ParseImportance(string? importance, string? priority)
    {
        if (importance is not null)
        {
            return importance.Trim().ToLowerInvariant() switch
            {
                "high" => EmailImportance.High,
                "low" => EmailImportance.Low,
                _ => EmailImportance.Normal,
            };
        }

        // X-Priority: 1 (highest) to 5 (lowest)
        return priority?.TrimStart() switch
        {
            ['1' or '2', ..] => EmailImportance.High,
            ['4' or '5', ..] => EmailImportance.Low,
            _ => EmailImportance.Normal,
        };
    }

    private static string? TrimId(string? id) => id?.Trim().TrimStart('<').TrimEnd('>');

    private static void AddRange<T>(IList<T> list, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            list.Add(item);
        }
    }
}
