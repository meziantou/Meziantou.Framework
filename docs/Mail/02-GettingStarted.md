---
title: Getting started
description: Install the package, and convert a message between two formats with the EmailMessage model.
---

## Install the package

Add the `Meziantou.Framework.Mail` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Mail" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Mail;`.

## Convert a message

[EmailMessage](cref:Meziantou.Framework.Mail.EmailMessage) holds what the formats have in common: the addresses, the
subject, the dates, the identifiers of the thread, the text, HTML and RTF bodies, the attachments and the embedded
messages. [FromPst](cref:Meziantou.Framework.Mail.EmailMessage.FromPst) and
[FromMime](cref:Meziantou.Framework.Mail.EmailMessage.FromMime) create it from an item of an Outlook data file or from a
MIME message, and [ToMime](cref:Meziantou.Framework.Mail.EmailMessage.ToMime) and
[ToPstBuilder](cref:Meziantou.Framework.Mail.EmailMessage.ToPstBuilder) write it back:

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/EmailMessageTests.cs;region=EmailMessage_Convert;format=md-fenced-code-block;dedent=true -->
```csharp
// An item of an Outlook data file, saved as an .eml file
using (var file = PstFile.Open(pstPath))
{
    var item = file.GetFolder("Inbox")!.EnumerateItems().First();
    EmailMessage.FromPst(item).ToMime().WriteTo(emlPath);
}

// The .eml file, read back
var email = EmailMessage.FromMime(MimeMessage.Load(emlPath));
Assert.Equal("Welcome to Microsoft Office Outlook 2003", email.Subject);
Assert.HasCount(11, email.Attachments);
```
<!-- endref -->

The other headers of a message are kept in [Headers](cref:Meziantou.Framework.Mail.EmailMessage.Headers). What a format
cannot store is lost when a message is converted to it: a MIME message has no RTF body, and an address without an
address part (only a name) cannot be written in a MIME header.
