---
title: Introduction
description: Read and write CSV data row by row, with or without a header row, with any separator and quote character.
---

`Meziantou.Framework.Csv` reads and writes CSV data. `CsvReader` reads rows from any `TextReader`, such as a
`StreamReader` over a file, and `CsvWriter` writes rows to any `TextWriter`. Every value is a `string`: the library
splits and quotes the data, and you convert the values to the types you need.

## What you can do with it

- **Read rows** one at a time, without loading the whole file, and get each value by position or by the name of its
  column. See [Reading CSV](03-ReadingCsv.md).
- **Read and write other formats** that share the CSV rules, such as tab-separated values, with your own separator and
  quote characters.
- **Handle quoted values**: separators, quotes and line breaks inside a value are read back as they were written.
- **Write rows** at once or value by value, and let the writer quote the values that need it. See
  [Writing CSV](04-WritingCsv.md).

## Namespace

Every type is in the `Meziantou.Framework.Csv` namespace: `CsvReader`, `CsvRow`, `CsvColumn` and `CsvWriter`.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, read a file and write one.
- [Limitations](05-Limitations.md): what the library does not do.
