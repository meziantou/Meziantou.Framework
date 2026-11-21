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

`make-eml.js` writes these files: run `node make-eml.js <directory>`. `MimeOracleTests` compares what the reader reads
in these files with what [MimeKit](https://github.com/jstedfast/MimeKit) reads.

[RFC 2047]: https://www.rfc-editor.org/rfc/rfc2047
[RFC 2231]: https://www.rfc-editor.org/rfc/rfc2231
[RFC 6532]: https://www.rfc-editor.org/rfc/rfc6532
