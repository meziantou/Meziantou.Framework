# MIME test files

The messages of this folder are written for these tests, under the MIT license of the repository. They do not come from
real mailboxes: public collections of messages (mailing list archives, the Enron corpus...) contain the mail of real
people. Each file covers a part of the format:

| File | Content |
| ---- | ------- |
| `simple.eml` | A text message without MIME headers |
| `encoded-words.eml` | Encoded words ([RFC 2047]) in base64 and quoted-printable, in UTF-8 and Latin charsets |
| `utf8-headers.eml` | Headers in UTF-8 ([RFC 6532]) |
| `groups-and-comments.eml` | Address groups, comments, quoted names and an obsolete date |
| `quoted-printable.eml` | A quoted-printable body with soft line breaks |
| `latin1-body.eml` | A text body in ISO-8859-1 |
| `lf-line-endings.eml` | A message with LF line breaks |
| `alternative.eml` | A `multipart/alternative` with a text and an HTML body, a preamble and an epilogue |
| `related-inline.eml` | A `multipart/related` with an HTML body and an inline image |
| `attachments.eml` | A `multipart/mixed` with attachments, file names in [RFC 2231] and in encoded words |
| `nested.eml` | A `multipart/alternative` in a `multipart/mixed` |
| `digest.eml` | A `multipart/digest`, whose parts are messages by default |
| `embedded-message.eml` | A forwarded `message/rfc822` |
| `uuencode.eml` | An attachment in uuencode |
| `unclosed-multipart.eml` | A multipart without its close delimiter |
| `malformed.eml` | Invalid headers and dates, a multipart without a boundary |

`make-eml.js` writes these files: run `node make-eml.js <directory>`. `MimeMessageTests` and `EmailMessageTests` check
that the messages are written back unchanged and that they convert to and from Outlook items.

## mime4j

The files of the [Mime4j](Mime4j) folder come from the test data of [Apache James Mime4j](https://github.com/apache/james-mime4j),
tag [`apache-mime4j-project-0.8.15`](https://github.com/apache/james-mime4j/tree/apache-mime4j-project-0.8.15/core/src/test/resources/testmsgs),
folder `core/src/test/resources/testmsgs`, under the Apache License 2.0 (see [LICENSE-mime4j.txt](Mime4j/LICENSE-mime4j.txt)
and [NOTICE-mime4j.txt](Mime4j/NOTICE-mime4j.txt)). Their content is unchanged; the messages are renamed from `.msg` to
`.eml`. For each message, `<name>_decoded.xml` is the structure mime4j expects, and `<name>_decoded_<path>.txt` or `.bin`
is the decoded content of each part.

The other files of the folder are not copied: `example.msg` is a message of a real person from a mailing list, the
`.out` and `.xml` files describe the output of the writer and of the raw parser of mime4j, and the `mimetools-testmsgs`
folder is under the Artistic License of MIME-tools.

`MimeMessageTests.Mime4j_MatchesExpectedStructure` compares the structure, the names of the headers and the decoded
content of the parts with what mime4j expects. The messages the reader reads differently are listed in the test, with
the reason.

[RFC 2047]: https://www.rfc-editor.org/rfc/rfc2047
[RFC 2231]: https://www.rfc-editor.org/rfc/rfc2231
[RFC 6532]: https://www.rfc-editor.org/rfc/rfc6532
