# MSG test files

The files come from the test data of [Apache POI](https://github.com/apache/poi), commit
[`f669279`](https://github.com/apache/poi/tree/f66927954a29cf1f5b86614fdf1816e6321c8aea/test-data/hsmf), folder
`test-data/hsmf`, under the Apache License 2.0 (see [LICENSE-poi.txt](LICENSE-poi.txt)). They are copied unchanged; only
`poi-` is added before their names.

| File | Content |
| ---- | ------- |
| `poi-quick.msg`, `poi-simple_test_msg.msg` | Simple emails |
| `poi-attachment_test_msg.msg` | An email with attachments |
| `poi-58214_with_attachment.msg` | An email with an attached email |
| `poi-example_sent_regular.msg`, `poi-example_sent_unicode.msg` | The same email, in an ANSI and in a Unicode file |
| `poi-ASCII_CP1251_LCID1049.msg` | An ANSI file in the code page 1251 (Cyrillic), given by its internet code page |
| `poi-chinese-traditional.msg` | An ANSI file in the code page 950 (Traditional Chinese) |
| `poi-keywords.msg` | Categories, a multi-valued named property |
| `poi-msgClassAppointment.msg`, `poi-msgClassContact.msg`, `poi-msgClassTask.msg` | An appointment, a contact and a task |

`MsgFileTests` checks what the reader reads in these files, and that the files the writer writes read back the same.
It also reads back the compound files of the writer.
