# Mailbox test files

The files of this folder are written for these tests, under the MIT license of the repository: public mailboxes
(mailing list archives) contain the mail of real people, and there is no public summary of Thunderbird or .emlx file
under a permissive license. `make-mailbox.js` writes them: run `node make-mailbox.js <directory>`.

| File | Content |
| ---- | ------- |
| `mboxo.mbox`, `mboxrd.mbox`, `mboxcl.mbox`, `mboxcl2.mbox` | The same three messages in the four variants of mbox, with lines that start with `From `, `>From ` and `>>From ` |
| `mboxrd-crlf.mbox` | The mboxrd file, with CRLF line breaks |
| `message.emlx` | A message of Apple Mail with its property list (flags, dates, remote identifier) |
| `summary.msf` | A summary of Thunderbird: the dictionaries, the table of the messages, a value on two lines, escaped characters, a committed group and an aborted one |

`MboxTests` compares the messages the reader finds in the mbox files, and in the files the writer writes, with what the
mbox parser of [MimeKit](https://github.com/jstedfast/MimeKit) finds. No independent reader of .emlx files and Mork
databases is available as a .NET package: their tests check the values of the files and the round trips.
