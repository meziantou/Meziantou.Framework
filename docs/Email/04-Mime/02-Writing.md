---
title: Writing messages
description: Create a MIME message with its headers, its bodies and its attachments, and write a message without changing it.
---

Create a [MimeMessage](cref:Meziantou.Framework.Email.MimeMessage), set its headers and its
[Body](cref:Meziantou.Framework.Email.MimeMessage.Body), then write it with
[WriteTo](cref:Meziantou.Framework.Email.MimeMessage.WriteTo) or
[ToByteArray](cref:Meziantou.Framework.Email.MimeMessage.ToByteArray):

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/MimeMessageTests.cs;region=MimeMessage_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var message = new MimeMessage
{
    From = [new MailboxAddress("Alice", "alice@example.com")],
    To = [new MailboxAddress("Bob", "bob@example.com")],
    Subject = "Réunion à 10 h",
    Date = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(1)),
    MessageId = "meeting-1@example.com",
};

var body = new MimeMultipart("mixed");
var text = new MimePart();
text.SetText("Bonjour Bob,\nÀ demain.");
body.Parts.Add(text);

var attachment = new MimePart
{
    ContentType = new MimeContentType("application", "pdf"),
    ContentDisposition = new MimeContentDisposition(MimeContentDisposition.Attachment) { FileName = "ordre du jour.pdf" },
};
attachment.SetContent("%PDF-1.4"u8.ToArray());
body.Parts.Add(attachment);
message.Body = body;

var bytes = message.ToByteArray();
```
<!-- endref -->

- The headers that are not ASCII are encoded ([RFC 2047]), and so are the display names of the addresses and the
  parameters ([RFC 2231]). The long headers are folded at 78 characters.
- The name of a header must contain only printable ASCII characters other than the colon ([RFC 5322] 2.2):
  [MimeHeader.Create](cref:Meziantou.Framework.Email.MimeHeader.Create),
  [MimeHeader.CreateRaw](cref:Meziantou.Framework.Email.MimeHeader.CreateRaw) and the
  methods of [Headers](cref:Meziantou.Framework.Email.MimeMessage.Headers) that take a name throw an
  `ArgumentException` for another name. The line breaks of a value are replaced by spaces.
- The writer removes the whitespace at the ends of an address, and the characters a header cannot contain in it (`<`,
  `>` and the line breaks). An address without a display name is written in angle brackets when it still contains
  whitespace (Unicode whitespace such as U+00A0 included) or a special character: the reader removes the whitespace of
  an address written without angle brackets, and keeps it between them.
- [SetText](cref:Meziantou.Framework.Email.MimePart.SetText) and
  [SetContent](cref:Meziantou.Framework.Email.MimePart.SetContent) set the content of a part. The writer encodes it with
  the transfer encoding of the part, or chooses one when the part has none: none for a short ASCII text,
  quoted-printable for the other texts, base64 for the other contents.
- A multipart gets a random boundary when it has none, and a message gets `MIME-Version: 1.0`.

[EmailMessage.ToMime](cref:Meziantou.Framework.Email.EmailMessage.ToMime) creates the usual structure from the model: the
text and HTML bodies in a `multipart/alternative`, the inline images in a `multipart/related` with the HTML body, and the
attachments in a `multipart/mixed`.

## Writing a message that was read

A message read and written without changes keeps its bytes: the headers as they were (folding and encoding included), the
content of the parts in their transfer encoding, the preamble and the epilogue of the multiparts. Only the line breaks
before the delimiters of the multiparts are written as CRLF. A part whose content is set again is encoded again.

[RFC 2047]: https://www.rfc-editor.org/rfc/rfc2047
[RFC 2231]: https://www.rfc-editor.org/rfc/rfc2231
[RFC 5322]: https://www.rfc-editor.org/rfc/rfc5322
