---
title: Introduction
description: Read and write CSV data record by record, as UTF-16 or UTF-8, or map the records to .NET objects with a serializer.
---

`Meziantou.Framework.Csv` reads and writes CSV data, and maps the records to .NET objects. It is built for large files:
the reader finds the separators, quotes and line terminators with SIMD instructions, and gives the values as slices of
its buffer, so reading a record does not allocate.

## What you can do with it

- **Read records** one at a time from a string, memory, a `TextReader`, a `Stream` or a file, synchronously or
  asynchronously, and get each value by position or by the name of its column. See [Reading CSV](03-ReadingCsv.md).
- **Read UTF-8 data without decoding it**: [Utf8CsvReader](cref:Meziantou.Framework.Csv.Utf8CsvReader) gives the values as UTF-8 bytes and parses numbers
  directly from them.
- **Write records** value by value, with the values quoted when they need it, to a `TextWriter`, a `Stream`, an
  `IBufferWriter<T>` or a file. See [Writing CSV](04-WritingCsv.md).
- **Map records to objects** with [CsvSerializer](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer): classes, structs and records, attributes to rename,
  order and ignore columns, and converters for your own types. See [Serialization](05-Serialization.md) and
  [Converters](06-Converters.md).
- **Publish with Native AOT**: a source generator writes the serialization code when you build. See
  [Source generation](07-SourceGeneration.md).
- **Handle real-world files**: other separators such as tab and semicolon, separator detection, comment lines, empty
  lines, spaces around values, and multi-line quoted values.

## Namespaces

- [Meziantou.Framework.Csv](cref:Meziantou.Framework.Csv): [CsvReader](cref:Meziantou.Framework.Csv.CsvReader), [Utf8CsvReader](cref:Meziantou.Framework.Csv.Utf8CsvReader), [CsvWriter](cref:Meziantou.Framework.Csv.CsvWriter), [Utf8CsvWriter](cref:Meziantou.Framework.Csv.Utf8CsvWriter) and their options.
- [Meziantou.Framework.Csv.Serialization](cref:Meziantou.Framework.Csv.Serialization): [CsvSerializer](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer), its options, the attributes and the converters.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, read a file and write one.
- [Performance](08-Performance.md): choose the APIs that do the least work.
- [Limitations](09-Limitations.md): what the library does not do.
