using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Fuzz;

/// <summary>
/// The MIME reader reads any input without throwing (except <see cref="EmailResourceLimitException"/>), and a message written
/// without changes reads back the same. Converting the model to MIME and back is stable: the first conversion of a malformed
/// message can lose what MIME cannot express (an empty address, an invalid name), the next ones change nothing. The encoders
/// round trip.
/// </summary>
public static partial class EmailFuzzTargets
{
    private const string MimeSeedPrefix = "TestData/Mime/";

    private static readonly string[] MimeTokens =
    [
        "From: ", "To: ", "Cc: ", "Subject: ", "Date: ", "Message-ID: ", "MIME-Version: 1.0", "Content-Type: ", "Content-Transfer-Encoding: ", "Content-Disposition: ", "Content-ID: ",
        "multipart/mixed", "multipart/alternative", "multipart/related", "multipart/digest", "message/rfc822", "text/plain", "text/html", "application/octet-stream",
        "; boundary=", "; charset=", "; name=", "; filename=", "filename*0*=", "filename*=utf-8''", "base64", "quoted-printable", "x-uuencode", "8bit", "attachment", "inline",
        "=?utf-8?B?", "=?iso-8859-1?Q?", "?=", "=\r\n", "=E9", "\r\n\r\n", "\r\n--", "--\r\n", "\r\n ", "<", ">", "\"", "(", ")", ":", ";", ",", "@", "begin 644 ", "end",
    ];

    private static readonly MimeReadOptions MimeOptions = new() { MaxDepth = 16, MaxParts = 256, MaxSize = 1024 * 1024 };

    private static IReadOnlyList<FuzzTarget> MimeTargets { get; } =
    [
        new("mime-read", MimeRead) { Seeds = GetMimeSeeds, Dictionary = MimeTokens },
        new("mime-roundtrip", MimeRoundTrip) { Seeds = GetMimeSeeds, Dictionary = MimeTokens },
        new("mime-encodings", MimeEncodings) { Seeds = GetMimeEncodingSeeds, Dictionary = MimeTokens },
    ];

    private static void MimeRead(ReadOnlySpan<byte> data)
    {
        MimeMessage message;
        try
        {
            message = MimeMessage.Load(data.ToArray(), MimeOptions);
        }
        catch (EmailResourceLimitException)
        {
            return;
        }

        var description = DescribeMime(message);

        // A message written without changes reads back the same
        var written = message.ToByteArray();
        FuzzAssert.Equal(string.Join('\n', description), string.Join('\n', DescribeMime(MimeMessage.Load(written, MimeOptions))), "The message written without changes reads differently");
    }

    private static void MimeRoundTrip(ReadOnlySpan<byte> data)
    {
        EmailMessage email;
        try
        {
            email = EmailMessage.FromMime(MimeMessage.Load(data.ToArray(), MimeOptions));
        }
        catch (EmailResourceLimitException)
        {
            return;
        }

        var first = EmailMessage.FromMime(MimeMessage.Load(email.ToMime().ToByteArray(), new MimeReadOptions()));
        var second = EmailMessage.FromMime(MimeMessage.Load(first.ToMime().ToByteArray(), new MimeReadOptions()));
        FuzzAssert.Equal(string.Join('\n', DescribeEmail(first)), string.Join('\n', DescribeEmail(second)), "The message written from the model reads differently");
        CheckOutlookAddresses(first);
    }

    /// <summary>The author, the sender and the reply addresses of a message are kept in an Outlook item: the sender is dropped when it is the author.</summary>
    private static void CheckOutlookAddresses(EmailMessage email)
    {
        MailboxAddress?[] addresses = [email.From, email.Sender, .. email.ReplyTo];
        if (addresses.Any(address => address is not null && (!address.Address.Contains('@', StringComparison.Ordinal) || $"{address.Name}{address.Address}".Contains('\0', StringComparison.Ordinal))))
        {
            // An address without @ is compared by name with the sender, as the Exchange address of the sender; one-off entries end their strings with a null character
            return;
        }

        var source = new EmailMessage { From = email.From, Sender = email.Sender };
        foreach (var address in email.ReplyTo)
        {
            source.ReplyTo.Add(address);
        }

        var copy = EmailMessage.FromMapi(MsgFile.Load(MsgFile.ToByteArray(source.ToMapiBuilder())));
        var from = email.From ?? email.Sender;
        var sender = email.Sender is null || from is null || string.Equals(email.Sender.Address, from.Address, StringComparison.OrdinalIgnoreCase) ? null : email.Sender;
        var replyTo = email.ReplyTo.Select(address => new MailboxAddress(string.Equals(address.Name ?? address.Address, address.Address, StringComparison.OrdinalIgnoreCase) ? null : address.Name, address.Address).ToString());
        FuzzAssert.Equal(from?.ToString(), copy.From?.ToString(), "The author differs in an Outlook item");
        FuzzAssert.Equal(sender?.ToString(), copy.Sender?.ToString(), "The sender differs in an Outlook item");
        FuzzAssert.Equal(string.Join("|", replyTo), string.Join("|", copy.ReplyTo.Select(address => address.ToString())), "The reply addresses differ in an Outlook item");
    }

    private static void MimeEncodings(ReadOnlySpan<byte> data)
    {
        FuzzAssert.True(MimeEncoding.DecodeQuotedPrintable(MimeEncoding.EncodeQuotedPrintable(NormalizeLineBreaks(data))).AsSpan().SequenceEqual(NormalizeLineBreaks(data)), "Quoted-printable does not round trip");
        FuzzAssert.True(MimeEncoding.DecodeBase64(MimeEncoding.EncodeBase64(data)).AsSpan().SequenceEqual(data), "Base64 does not round trip");

        var text = FuzzText.Decode(data).ReplaceLineEndings(" ");
        if (IsWellFormed(text))
        {
            var encoded = MimeEncoding.EncodeHeaderText(text);
            FuzzAssert.True(MimeEncoding.IsPrintableAscii(encoded), "An encoded header is not printable ASCII");
            FuzzAssert.Equal(text, MimeEncoding.DecodeHeaderValue(encoded), "An encoded header does not decode to its text");

            var contentType = new MimeContentType("application", "octet-stream") { Name = text };
            FuzzAssert.Equal(text, MimeContentType.Parse(contentType.ToString()).Name, "A parameter does not round trip");

            var address = new MailboxAddress(text, "user@example.com");
            FuzzAssert.Equal(text.Length == 0 ? null : text, MailboxAddress.Parse(address.ToHeaderValue())?.Name, "A display name does not round trip");
        }

        // The parsers of structured values never throw
        _ = MimeDate.Parse(text);
        _ = MailboxAddress.ParseList(text);
        _ = MimeContentDisposition.Parse(text).FileName;
    }

    private static List<string> DescribeMime(MimeMessage message)
    {
        var result = new List<string>
        {
            "headers: " + string.Join("|", message.Headers.Select(header => header.Name + "=" + header.Value)),
            "addresses: " + string.Join("|", message.From.Concat(message.To).Concat(message.Cc).Concat(message.Bcc).Concat(message.ReplyTo).Select(address => address.ToString())),
            "fields: " + message.Subject + "|" + message.Date + "|" + message.MessageId + "|" + message.InReplyTo + "|" + string.Join(' ', message.References) + "|" + message.Sender,
            "text: " + message.TextBody,
            "html: " + message.HtmlBody,
        };

        DescribeEntity(message.Body, result, "", 0);
        return result;
    }

    private static void DescribeEntity(MimeEntity entity, List<string> result, string indent, int depth)
    {
        var disposition = entity.ContentDisposition;
        result.Add(indent + entity.ContentType + "|" + disposition + "|" + entity.ContentId);
        switch (entity)
        {
            case MimePart part:
                result.Add(indent + "  " + part.FileName + "|" + Convert.ToHexString(part.GetContent()) + "|" + part.GetText().Length);
                break;
            case MimeMultipart multipart when depth < 32:
                foreach (var child in multipart.Parts)
                {
                    DescribeEntity(child, result, indent + "  ", depth + 1);
                }

                break;
            case MimeMessagePart messagePart when depth < 32:
                result.AddRange(DescribeMime(messagePart.Message).Select(line => indent + "  " + line));
                break;
        }
    }

    private static List<string> DescribeEmail(EmailMessage email)
    {
        var result = new List<string>
        {
            email.Subject + "|" + email.From + "|" + email.Sender + "|" + email.Date + "|" + email.MessageId + "|" + email.InReplyTo + "|" + string.Join(' ', email.References) + "|" + email.Importance,
            string.Join(", ", email.To) + "|" + string.Join(", ", email.Cc) + "|" + string.Join(", ", email.Bcc) + "|" + string.Join(", ", email.ReplyTo) + "|" + string.Join(", ", email.Categories),
            "text: " + email.TextBody?.ReplaceLineEndings("\n"),
            "html: " + email.HtmlBody?.ReplaceLineEndings("\n"),
        };

        foreach (var attachment in email.Attachments)
        {
            result.Add(attachment.FileName + "|" + attachment.ContentType + "|" + attachment.ContentId + "|" + attachment.IsInline + "|" + Convert.ToHexString(attachment.Content.Span));
            if (attachment.EmbeddedMessage is { } embedded)
            {
                result.AddRange(DescribeEmail(embedded).Select(line => "  " + line));
            }
        }

        return result;
    }

    /// <summary>Quoted-printable writes CRLF line breaks: a lone CR or LF of the input becomes CRLF.</summary>
    private static byte[] NormalizeLineBreaks(ReadOnlySpan<byte> data)
    {
        var text = Encoding.Latin1.GetString(data).ReplaceLineEndings("\r\n");
        return Encoding.Latin1.GetBytes(text);
    }

    /// <summary>A string without unpaired surrogates, which UTF-8 cannot encode.</summary>
    private static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<FuzzSeed> GetMimeSeeds()
    {
        var assembly = typeof(EmailFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(MimeSeedPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return new FuzzSeed(name[MimeSeedPrefix.Length..], memory.ToArray());
        }
    }

    private static IEnumerable<FuzzSeed> GetMimeEncodingSeeds()
    {
        yield return FuzzSeed.FromText("ascii", "Hello world");
        yield return FuzzSeed.FromText("accents", "Réunion à Zürich 😀 = ? _");
        yield return FuzzSeed.FromText("encoded-word", "=?utf-8?q?caf=C3=A9?=");
        yield return FuzzSeed.FromText("date", "Tue, 1 Jul 2003 10:52:37 +0200");
        yield return FuzzSeed.FromText("addresses", "\"Smith, John\" <john@example.com>, Team: a@example.com;");
        yield return new FuzzSeed("binary", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
    }
}
