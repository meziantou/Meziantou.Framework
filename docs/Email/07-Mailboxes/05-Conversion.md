---
title: Convert mailboxes
description: Convert a mailbox with its folders between Outlook data files, the mbox files of Thunderbird and Maildir folders.
---

[MailboxConverter.Convert](cref:Meziantou.Framework.Email.Storage.MailboxConverter.Convert) converts a mailbox, with its
tree of folders, to another [format](cref:Meziantou.Framework.Email.Storage.MailboxFormat): to migrate from Outlook to
Thunderbird, to archive a Maildir folder in an Outlook data file...

<!-- ref:../../../tests/Meziantou.Framework.Email.Tests/MailboxConverterTests.cs;region=MailboxConverter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// An Outlook data file to the folders of Thunderbird, then to a Maildir folder
var result = MailboxConverter.Convert(pstPath, mboxPath, MailboxFormat.Mbox);
Assert.Empty(result.Errors);
Assert.True(File.Exists(Path.Combine(mboxPath, "Inbox")));

var copy = MailboxConverter.Convert(mboxPath, maildirPath, MailboxFormat.Maildir);
Assert.Equal(result.MessageCount, copy.MessageCount);
Assert.Equal(MailboxFormat.Maildir, MailboxConverter.DetectFormat(maildirPath));
```
<!-- endref -->

| Format | Source | Folders |
| --- | --- | --- |
| Outlook data file | A `.pst` or `.ost` file | The folders under the top of the personal folders; the search folders are skipped |
| mbox | A directory, or a single mbox file | A file per folder, and the subfolders of a folder in a directory `<name>.sbd`, as Thunderbird stores them; the summaries (`.msf`) and the other files are skipped |
| Maildir | A directory with `cur`, `new` and `tmp` | The root is the inbox, and the subfolders are Maildir++ folders: `.Archive.2026` is the subfolder `2026` of `Archive` |

[DetectFormat](cref:Meziantou.Framework.Email.Storage.MailboxConverter.DetectFormat) gives the format of the source. The
destination must not exist, or be an empty directory.

- The messages of mbox and Maildir folders are copied as they are between these formats, with the LF line breaks of
  the mbox files. They are converted to and from the items of an Outlook data file through
  [EmailMessage](cref:Meziantou.Framework.Email.EmailMessage): what one of the formats cannot store is lost (see
  [Getting started](../02-GettingStarted.md)).
- The read state is kept: the `Seen` flag of Maildir, the read flag of Outlook, and the `Status` header of mbox
  (`Status: RO`), which the converter adds to the messages that are read.
- An Outlook data file has items that are not emails: contacts, appointments, tasks, notes... An Outlook data file
  destination keeps them; an mbox or Maildir destination skips them and counts them in
  [SkippedItemCount](cref:Meziantou.Framework.Email.Storage.MailboxConversionResult.SkippedItemCount), unless
  [IncludeNonMailItems](cref:Meziantou.Framework.Email.Storage.MailboxConversionOptions.IncludeNonMailItems) writes them
  as email messages. Meeting requests are emails.
- A message that cannot be read or written is skipped, and reported in
  [Errors](cref:Meziantou.Framework.Email.Storage.MailboxConversionResult.Errors) with its folder.
- An Outlook data file the converter writes has the folders Outlook requires, such as Deleted Items.
