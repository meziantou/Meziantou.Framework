---
title: Mailboxes and email messages
description: Convert a mailbox with its folders between Outlook data files (PST, OST), mbox and Maildir, export it to .eml or .msg files, and convert email messages between EML, MSG, EMLX and winmail.dat.
---

The tool converts mailboxes with their folders, and email messages, with
[Meziantou.Framework.Email](../Email/01-Introduction.md):

```bash
Meziantou.Framework.FileConversion.Tool archive.pst --output-format mbox
Meziantou.Framework.FileConversion.Tool ~/Maildir --output mail.pst
Meziantou.Framework.FileConversion.Tool archive.pst --output-format eml
Meziantou.Framework.FileConversion.Tool message.msg --output-format eml
```

The first command writes the folder `archive.mbox`, the second the Outlook data file `mail.pst`, the third the folder
`archive.eml` with one `.eml` file per message, and the last one `message.eml`.

| Format | Extensions | Read | Write | Output |
| --- | --- | --- | --- | --- |
| PST | `.pst` | Yes | Yes | An Outlook data file |
| OST | `.ost` | Yes | Yes | An offline Outlook data file |
| Mbox | `.mbox`, `.mbx` | Yes | Yes | A folder of mbox files, in the layout of Thunderbird |
| Maildir | `.maildir` | Yes | Yes | A Maildir folder |
| EML | `.eml` | Yes | Yes | An email message, or a folder of `.eml` files for a mailbox |
| MSG | `.msg` | Yes | Yes | An Outlook item, or a folder of `.msg` files for a mailbox |
| OFT | `.oft` | Yes | Yes | An Outlook template |
| EMLX | `.emlx` | Yes | Yes | An Apple Mail message |
| TNEF | `.tnef` | Yes | Yes | A TNEF stream (`winmail.dat`) |

The format of an input is detected from its content: a `winmail.dat` file is read as TNEF. A folder is a mailbox: a
Maildir folder when it has the `cur`, `new` and `tmp` folders, and a folder of mbox files otherwise. Only the
conversions to a mailbox or to message files read a folder.

## Mailboxes

Each input mailbox gives one output mailbox, with the same tree of folders.

| Input | Folders |
| --- | --- |
| PST, OST | The folders under the top of the personal folders; the search folders are skipped |
| An mbox file | One folder, named after the file |
| A folder of mbox files | A file per folder, and the subfolders of a folder in a folder `<name>.sbd`, as Thunderbird stores them |
| Maildir | The root is the inbox, and the Maildir++ folders (`.Archive.2026`) are its subfolders |

The output of an mbox or a Maildir conversion is a folder: `archive.pst --output-format mbox` writes `archive.mbox/Inbox`,
`archive.mbox/Inbox.sbd/Projects`... An EML or MSG output of a mailbox is a folder too, with one folder per mailbox
folder, and a file per message named after its subject. See
[Convert mailboxes](../Email/07-Mailboxes/06-Conversion.md) for what each conversion keeps: the read state, the folder
names, the items that are not emails.

```bash
Meziantou.Framework.FileConversion.Tool archive.pst --output thunderbird --output-format mbox
Meziantou.Framework.FileConversion.Tool archive.ost --output archive.pst
Meziantou.Framework.FileConversion.Tool archive.pst --output-format msg
```

- An Outlook data file has items that are not emails: contacts, appointments, tasks, notes... A PST, OST or MSG output
  keeps them. An mbox, Maildir or EML output skips them, and the tool reports how many on the error output, unless
  `--email-include-non-mail-items` writes them as messages.
- A message that cannot be read or written is skipped, and reported on the error output with its folder. The other
  messages are converted.
- The tool does not replace a folder, even with `--overwrite`: a folder output must not exist, or be empty. This way, a
  wrong `--output` cannot delete a folder. `--overwrite` replaces a PST or OST file.
- The output is written next to its final path, then moved: a conversion that fails leaves no partial mailbox.

### Outlook data files

A PST output opens in Outlook (File › Open › Outlook Data File).

An OST output has the 4 KB-page format of the offline files of Outlook 2013 and later. Outlook does not open an OST file
on its own, and does not attach it to an account: an OST file is the local copy of an Exchange, Microsoft 365 or IMAP
account, which Outlook makes itself. Write an OST file for the tools that read OST files; write a PST file to open the
mailbox in Outlook.

| Option | Applies to | Description |
| --- | --- | --- |
| `--pst-format` | PST output | `Unicode` (the default, Outlook 2003 and later) or `Ansi` (Outlook 97 to 2002, files up to 2 GB) |
| `--pst-encryption` | PST and OST outputs | `Permute` (the default of Outlook), `Cyclic` or `None`. None of them needs a password: they do not protect the data |

### mbox files

`--mbox-format` sets the variant of the mbox files the tool reads and writes, that is how the lines of a message that
start with `From ` are escaped: `Mboxrd` (the default, the format of most mail tools), `Mboxo`, `Mboxcl` or `Mboxcl2`.

## Email messages

Each input message gives one output message, next to it by default:

```bash
Meziantou.Framework.FileConversion.Tool winmail.dat --output-format msg
Meziantou.Framework.FileConversion.Tool "inbox/*.eml" --output "outlook/{name}.msg"
Meziantou.Framework.FileConversion.Tool message.emlx --output message.eml
```

- An email message keeps its bytes in an EML output, with CRLF line breaks: an EMLX file of Apple Mail has LF line
  breaks.
- The other conversions go through the model of the library, so what a format cannot store is lost. See
  [Getting started with Meziantou.Framework.Email](../Email/02-GettingStarted.md).
- An Outlook item that is not an email, such as a contact, converts to EML as a message.
- An EML input is read in an MSG or EMLX output, since the EML format does not store the read state.

A message does not convert to a mailbox: only a mailbox does.
