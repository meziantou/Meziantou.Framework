---
title: mbox files
description: Read the messages of an mbox file one at a time, in its four variants, and write messages in an mbox file.
---

An mbox file is a mailbox of Unix mail tools and of Thunderbird: the messages one after the other, each one after a line
that starts with `From ` and gives the envelope sender and a date ([RFC 4155]).

## Read a file

[MboxReader.ReadMessages](cref:Meziantou.Framework.Email.Storage.MboxReader.ReadMessages) reads the messages of a file or of a
stream as they are enumerated, one at a time, so that a large mailbox is not read in memory. Each
[MboxMessage](cref:Meziantou.Framework.Email.Storage.MboxMessage) has its `From ` line, its
[Sender](cref:Meziantou.Framework.Email.Storage.MboxMessage.Sender) and its [Date](cref:Meziantou.Framework.Email.Storage.MboxMessage.Date),
its position in the file, its bytes, and the [Message](cref:Meziantou.Framework.Email.Storage.MboxMessage.Message), read when it is
accessed:

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/MboxTests.cs;region=MboxReader_ReadMessages;format=md-fenced-code-block;dedent=true -->
```csharp
foreach (var entry in MboxReader.ReadMessages(path, new MboxReadOptions { Format = MboxFormat.Mboxrd }))
{
    // The envelope sender and the date of the "From " line, then the message, read when it is accessed
    var sender = entry.Sender;
    var subject = entry.Message.Subject;
    Assert.NotNull(sender);
    Assert.NotNull(subject);
}
```
<!-- endref -->

The variants of the format differ in how a line of a message that starts with `From ` is kept apart from the `From ` line
of the next message. [MboxReadOptions.Format](cref:Meziantou.Framework.Email.Storage.MboxReadOptions.Format) gives the variant of
the file:

| [MboxFormat](cref:Meziantou.Framework.Email.Storage.MboxFormat) | End of a message | Escaping |
| --- | --- | --- |
| `Mboxrd` (default) | The next `From ` line | `>` before the lines that start with `From ` after any number of `>`: reversible |
| `Mboxo` | The next `From ` line | `>` before the lines that start with `From `: a line that started with `>From ` reads as `From ` |
| `Mboxcl` | The `Content-Length` header | As `Mboxo` |
| `Mboxcl2` | The `Content-Length` header | None |

A message without a `Content-Length` header ends at the next `From ` line in the last two variants. The blank line
before the `From ` line of the next message is not part of the message.
[MessageOptions](cref:Meziantou.Framework.Email.Storage.MboxReadOptions.MessageOptions) limits the size of a message. A file that
does not start with a `From ` line throws an [InvalidEmailDataException](cref:Meziantou.Framework.Email.InvalidEmailDataException).

## Write a file

[MboxWriter](cref:Meziantou.Framework.Email.Storage.MboxWriter) writes messages at the end of a file or of a stream: the `From `
line, the message with its lines escaped as the variant requires (and its `Content-Length` header for `Mboxcl` and
`Mboxcl2`), then a blank line. The lines end with LF.

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/MboxTests.cs;region=MboxWriter_Write;format=md-fenced-code-block;dedent=true -->
```csharp
using (var writer = MboxWriter.Create(path, MboxFormat.Mboxrd))
{
    var message = new MimeMessage { Subject = "Hello", From = [new MailboxAddress("Alice", "alice@example.com")] };
    var body = new MimePart();
    body.SetText("From the start of a line, escaped as >From in the file.");
    message.Body = body;
    writer.Write(message, date: new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
}

var entry = MboxReader.ReadMessages(path).Single();
Assert.Equal("From alice@example.com Thu Jan 15 09:00:00 2026", entry.FromLine);
Assert.StartsWith("From the start of a line", entry.Message.TextBody, StringComparison.Ordinal);
```
<!-- endref -->

[RFC 4155]: https://www.rfc-editor.org/rfc/rfc4155
