---
title: Overview
description: Read an email message (.eml) and its MIME parts: headers, addresses, bodies, attachments and embedded messages.
---

[MimeMessage](cref:Meziantou.Framework.Email.MimeMessage) is a message in the Internet Message Format ([RFC 5322]) with its
MIME structure ([RFC 2045] to [RFC 2049]): the content of an `.eml` file, of a file of a Maildir folder, or of a message
of an mbox file. [MimeMessage.Load](cref:Meziantou.Framework.Email.MimeMessage.Load) reads one from a path, a stream or
bytes:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/MimeMessageTests.cs;region=MimeMessage_Load;format=md-fenced-code-block;dedent=true -->
```csharp
var message = MimeMessage.Load(path);
Assert.Equal("Attachments", message.Subject);
Assert.Equal("alice@example.com", message.From[0].Address);
Assert.Equal("See the attachments.", message.TextBody?.TrimEnd());

foreach (var attachment in message.Attachments.OfType<MimePart>())
{
    // The file name is decoded (RFC 2047 and RFC 2231), and the content is decoded from base64 or quoted-printable
    var fileName = attachment.FileName;
    var content = attachment.GetContent();
    Assert.NotNull(content);
    _ = fileName;
}
```
<!-- endref -->

## Headers and addresses

[Headers](cref:Meziantou.Framework.Email.MimeMessage.Headers) are the headers of the message in their order, and the
members of the message read the common ones: [From](cref:Meziantou.Framework.Email.MimeMessage.From),
[To](cref:Meziantou.Framework.Email.MimeMessage.To), [Subject](cref:Meziantou.Framework.Email.MimeMessage.Subject),
[Date](cref:Meziantou.Framework.Email.MimeMessage.Date),
[MessageId](cref:Meziantou.Framework.Email.MimeMessage.MessageId)... The values are decoded: the encoded words of
[RFC 2047] (`=?utf-8?B?...?=`), the headers in UTF-8 ([RFC 6532]), the dates of [RFC 5322] and their obsolete forms, the
groups and the comments of the address lists.

## Parts

[Body](cref:Meziantou.Framework.Email.MimeMessage.Body) is the root of the MIME tree:

- a [MimePart](cref:Meziantou.Framework.Email.MimePart) is a leaf: a text or a file.
  [GetContent](cref:Meziantou.Framework.Email.MimePart.GetContent) decodes it from base64, quoted-printable or uuencode,
  and [GetText](cref:Meziantou.Framework.Email.MimePart.GetText) decodes a text with its charset;
- a [MimeMultipart](cref:Meziantou.Framework.Email.MimeMultipart) contains parts (`multipart/mixed`, `alternative`,
  `related`, `digest`...);
- a [MimeMessagePart](cref:Meziantou.Framework.Email.MimeMessagePart) contains a message (`message/rfc822`), such as a
  forwarded message.

[TextBody](cref:Meziantou.Framework.Email.MimeMessage.TextBody) and
[HtmlBody](cref:Meziantou.Framework.Email.MimeMessage.HtmlBody) are the first text and HTML parts that are not
attachments, and [Attachments](cref:Meziantou.Framework.Email.MimeMessage.Attachments) are the other leaves, including
the inline images of an HTML body. The parameters of
[MimeContentType](cref:Meziantou.Framework.Email.MimeContentType) and
[MimeContentDisposition](cref:Meziantou.Framework.Email.MimeContentDisposition), such as the file names, are decoded from
[RFC 2231] (`filename*=utf-8''...`) and from the encoded words some clients use.

## Malformed messages

The reader does not reject a malformed message: it reads what it can, as email clients do. A line that is not a header
ends the headers, a multipart without its close delimiter ends at the end of the message, a multipart without a boundary
has its content in its [Preamble](cref:Meziantou.Framework.Email.MimeMultipart.Preamble), and an unknown charset is read as
UTF-8, or Latin-1 when the bytes are not UTF-8. [MimeReadOptions](cref:Meziantou.Framework.Email.MimeReadOptions) limits
the depth of the tree, the number of parts and the size of a message read from a stream.

[RFC 2045]: https://www.rfc-editor.org/rfc/rfc2045
[RFC 2047]: https://www.rfc-editor.org/rfc/rfc2047
[RFC 2049]: https://www.rfc-editor.org/rfc/rfc2049
[RFC 2231]: https://www.rfc-editor.org/rfc/rfc2231
[RFC 5322]: https://www.rfc-editor.org/rfc/rfc5322
[RFC 6532]: https://www.rfc-editor.org/rfc/rfc6532
