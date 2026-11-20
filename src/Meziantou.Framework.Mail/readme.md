# Meziantou.Framework.Mail

Reads and writes Outlook data files: PST and OST, in the ANSI, Unicode and 4K-page formats, with or without encryption
([MS-PST]). No Outlook or MAPI installation is needed.

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

[MS-PST]: https://learn.microsoft.com/openspecs/office_file_formats/ms-pst/141923d5-15ab-4ef1-a524-6dce75aae546
[MS-OXRTFCP]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxrtfcp/65dfe2df-1b69-43fc-8ebd-21819a7463fb
