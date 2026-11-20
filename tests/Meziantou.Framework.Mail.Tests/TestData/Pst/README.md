# PST test files

Outlook is the only program that writes PST files, so the test files come from the test suites of other PST readers, and
from the writer of this library. The third-party files are copied unchanged; only their names are changed.

| File | Source | License | Content |
| ---- | ------ | ------- | ------- |
| `java-libpst-dist-list.pst` | [java-libpst](https://github.com/rjohnsondev/java-libpst), commit [`f158a64`](https://github.com/rjohnsondev/java-libpst/blob/f158a64acf2a0e46ac3bd699bc7a5a8da6c40d26/src/test/resources/dist-list.pst), `src/test/resources/dist-list.pst` | Apache-2.0 (java-libpst is licensed under both the LGPL and the Apache License 2.0), see [LICENSE-java-libpst.txt](LICENSE-java-libpst.txt) | A contact, a distribution list, a recurring appointment with deleted and modified occurrences |
| `java-libpst-passworded.pst` | java-libpst, same commit, `src/test/resources/passworded.pst` | Apache-2.0 | A file protected by a password |
| `java-libpst-example-2013.ost` | java-libpst, same commit, `src/test/resources/example-2013.ost` | Apache-2.0 | An OST file of Outlook 2013 (4 KB pages, compressed blocks) |
| `tika-variousBodyTypes.pst` | [Apache Tika](https://github.com/apache/tika), commit [`f1f785f`](https://github.com/apache/tika/blob/f1f785f533877ce7426f45b1460ed52e154bcaca/tika-parsers/tika-parsers-standard/tika-parsers-standard-modules/tika-parser-microsoft-module/src/test/resources/test-documents/testPST_variousBodyTypes.pst), `testPST_variousBodyTypes.pst` | Apache-2.0, see [LICENSE-tika.txt](LICENSE-tika.txt) | Messages with plain text, HTML and RTF bodies |
| `outlook-pst-rs-Empty.pst` | [outlook-pst-rs](https://github.com/microsoft/outlook-pst-rs), commit [`cfb721d`](https://github.com/microsoft/outlook-pst-rs/blob/cfb721daee538acc50d2bad6faac5ce724f6037d/crates/pst/examples/Empty.pst), `crates/pst/examples/Empty.pst` | MIT, see [LICENSE-outlook-pst-rs.txt](LICENSE-outlook-pst-rs.txt) | An empty file created by Outlook |
| `libyal-outlook.pst` | [libyal/testdata](https://github.com/libyal/testdata), commit [`f018eb7`](https://github.com/libyal/testdata/blob/f018eb7c91c8356123b021ecd28ae894dd500210/pst/outlook.pst), `pst/outlook.pst`, by Joachim Metz | CC BY 4.0, see [LICENSE-libyal.txt](LICENSE-libyal.txt) | A message of Outlook 2003 with 11 attachments, a folder with a CJK name |
| `writer-unicode-permute.pst`, `writer-unicode-cyclic.pst`, `writer-ansi-small.pst` | Written by `PstWriterTests.WriteSample` and `WriteSmallSample` | MIT | The items of the writer tests |

The writer files are the files the writer writes: `PstWriterTests.WriterSample_MatchesCommittedFile` fails when they
change. Run the tests with the `PST_UPDATE_TESTDATA` environment variable set to `1` to update them, then update
`expected.json`. There is no 4K file of the writer: such a file grows by 16 MB.

## expected.json

`expected.json` is the output of [Oracle/dump.mjs](Oracle/dump.mjs), which reads the files with
[pst-extractor](https://github.com/epfromer/pst-extractor) 1.12.0 (MIT), an independent reader ported from java-libpst:

```bash
npm install pst-extractor@1.12.0
node dump.mjs java-libpst-dist-list.pst java-libpst-passworded.pst libyal-outlook.pst outlook-pst-rs-Empty.pst tika-variousBodyTypes.pst java-libpst-example-2013.ost writer-ansi-small.pst writer-unicode-permute.pst > expected.json
```

`PstOracleTests` compares what the library reads with it. pst-extractor has limits, which the files and the tests take into
account:

- It does not read files with the cyclic encryption: `writer-unicode-cyclic.pst` is not in `expected.json`.
- It reads the tables of an ANSI file only when they fit in one block: the ANSI file of the writer is small.
- It skips the last entry of the name-to-ID map: the last named property of `writer-ansi-small.pst` is one it does not
  read.
- It returns no embedded message for some attachments: the test accepts the embedded messages the library reads there.
