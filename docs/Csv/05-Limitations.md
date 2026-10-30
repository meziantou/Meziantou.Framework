---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **Values are strings.** There is no mapping between rows and objects and no type conversion: parse the values
  yourself, with the invariant culture.
- **Asynchronous only.** Reading and writing are `async` methods. There is no synchronous API and no
  `CancellationToken`.
- **No trimming and no comments.** Spaces around a value are part of the value, and a line that starts with `#` is a
  row like any other.
- **No detection.** The reader does not guess the separator, the quote character, whether the first row is a header or
  the encoding. Set `Separator`, `Quote` and [HasHeaderRow](cref:Meziantou.Framework.Csv.CsvReader.HasHeaderRow), and pass a `TextReader` that decodes the right encoding.
- **Case-sensitive column names.** `row["name"]` does not find the column `Name`; see
  [Rows with a header](03-ReadingCsv.md#rows-with-a-header).
- **No validation.** Rows can have any number of values. Check `row.Values.Count` when the data must have a fixed number
  of columns.
