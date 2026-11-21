# Meziantou.Framework.Mail

Reads and writes email files, without native dependencies:

- Outlook data files: PST and OST, in the ANSI, Unicode and 4K-page formats, with or without encryption ([MS-PST]).
  No Outlook or MAPI installation is needed.
- Email messages (`.eml`): the Internet Message Format and MIME, with the encoded headers, the multiparts, the
  attachments and the embedded messages. A message read and written without changes keeps its bytes.
- Outlook items (`.msg`, [MS-OXMSG]), read and written with the types of the items of Outlook data files.
- TNEF streams (`winmail.dat`, [MS-OXTNEF]), read and written with the same types; the `winmail.dat` attachments of
  MIME messages are expanded when they are converted to `EmailMessage`.
- `EmailMessage`, a model to convert a message from one format to another.

Outlook data files:

- Folders, search folders and associated items
- Emails: properties, recipients, bodies in plain text, HTML and RTF (compressed RTF, [MS-OXRTFCP])
- Attachments, read block by block, and embedded messages
- Contacts, distribution lists, appointments with their recurrence and exceptions, tasks
- Every MAPI property, including the named properties
- A writer that creates new files, and copies items from other files
- A validator that checks the structures of a file

## Read a file

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs;region=PstFolder_EnumerateItems;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var subjects = new List<string>();
void Visit(PstFolder folder)
{
    foreach (var item in folder.EnumerateItems())
    {
        subjects.Add($"{folder.Path}: {item.Subject}");
    }

    foreach (var child in folder.Folders)
    {
        Visit(child);
    }
}

Visit(file.RootFolder);
Assert.Contains("Top of Outlook data file/Inbox/tmp: original email", subjects);
```
<!-- endref -->

## Read the attachments

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs;region=PstAttachment_Save;format=md-fenced-code-block;dedent=true -->
```csharp
using var file = PstFile.Open(path);
var message = file.GetFolder("Inbox")!.EnumerateItems().First();
foreach (var attachment in message.Attachments)
{
    if (attachment.Method == PstAttachmentMethod.ByValue)
    {
        // Read the content block by block, without loading it in memory
        using var content = attachment.OpenRead();
        using var output = File.Create(Path.Combine(directory.FullName, Path.GetFileName(attachment.Name ?? "attachment")));
        content.CopyTo(output);
    }
}
```
<!-- endref -->

## Write a file

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/PstWriterTests.cs;region=PstWriter_Create;format=md-fenced-code-block;dedent=true -->
```csharp
using (var writer = PstWriter.Create(path, new PstWriterOptions { DisplayName = "Archive" }))
{
    var inbox = writer.RootFolder.CreateFolder("Inbox", "IPF.Note");
    var message = new PstMessageBuilder
    {
        Subject = "Hello",
        Body = "Hello from Meziantou.Framework.Mail",
        SenderName = "Alice",
        SenderEmailAddress = "alice@example.com",
        SenderAddressType = "SMTP",
        ClientSubmitTime = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
    };
    message.Recipients.Add(new PstRecipientBuilder("Bob", "bob@example.com"));
    message.Attachments.Add(PstAttachmentBuilder.FromContent("notes.txt", "Some notes"u8.ToArray(), "text/plain"));
    inbox.AddItem(message);

    var contacts = writer.RootFolder.CreateFolder("Contacts", "IPF.Contact");
    contacts.AddItem(new PstContactBuilder { GivenName = "Bob", Surname = "Smith", Email1Address = "bob@example.com" });
}

using var file = PstFile.Open(path);
var item = file.GetFolder("Inbox")!.EnumerateItems().Single();
Assert.Equal("Hello", item.Subject);
Assert.Equal("bob@example.com", item.Recipients.Single().EmailAddress);
Assert.Equal("notes.txt", item.Attachments.Single().Name);
```
<!-- endref -->

## Read an email message

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/MimeMessageTests.cs;region=MimeMessage_Load;format=md-fenced-code-block;dedent=true -->
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

## Save an Outlook item as an .msg file

<!-- ref:../../tests/Meziantou.Framework.Mail.Tests/MsgFileTests.cs;region=MsgFile_Save;format=md-fenced-code-block;dedent=true -->
```csharp
var message = new PstMessageBuilder
{
    Subject = "Quarterly report",
    Body = "The report is attached.",
    SenderName = "Alice",
    SenderEmailAddress = "alice@example.com",
    SenderAddressType = "SMTP",
};
message.Recipients.Add(new PstRecipientBuilder("Bob", "bob@example.com"));
message.Attachments.Add(PstAttachmentBuilder.FromContent("report.csv", "Quarter,Total\nQ1,42\n"u8.ToArray(), "text/csv"));
MsgFile.Save(message, path);

var item = MsgFile.Load(path);
Assert.Equal("Quarterly report", item.Subject);
Assert.Equal("bob@example.com", item.Recipients.Single().EmailAddress);
Assert.Equal("report.csv", item.Attachments.Single().Name);
```
<!-- endref -->

## Convert a message

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

[MS-PST]: https://learn.microsoft.com/openspecs/office_file_formats/ms-pst/141923d5-15ab-4ef1-a524-6dce75aae546
[MS-OXMSG]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxmsg/b046868c-9fbf-41ae-9ffb-8de2bd4eec82
[MS-OXTNEF]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxtnef/1f0544d7-30b7-4194-b58f-adc82f3763bb
[MS-OXRTFCP]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxrtfcp/65dfe2df-1b69-43fc-8ebd-21819a7463fb
