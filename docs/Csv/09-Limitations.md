---
title: Limitations
description: What the library does not do, so you can plan around it.
---

- **Flat records only.** The serializer maps each column to a member of a simple type. A member of a class or a
  collection type needs a converter that turns it into one value.
- **No polymorphism.** The serializer creates instances of the declared type.
- **No encoding detection.** [Utf8CsvReader](cref:Meziantou.Framework.Csv.Utf8CsvReader) and the `Stream` overloads of [CsvSerializer](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer) read UTF-8. [CsvReader.Create](cref:Meziantou.Framework.Csv.CsvReader.Create)
  detects the encoding from a byte order mark only; pass the encoding otherwise.
- **ASCII separators for UTF-8.** The separator, the quote and the comment characters of the UTF-8 readers and writers
  must be ASCII characters.
- **One character separators.** The separator and the quote are single characters; multi-character separators such as
  `||` are not supported.
- **No header detection.** Set [HasHeaderRow](cref:Meziantou.Framework.Csv.CsvReaderOptions.HasHeaderRow) to match the data.
- **Spaces only.** [Trim](cref:Meziantou.Framework.Csv.CsvReaderOptions.Trim) removes the space character, not tabs or other white spaces.
- **No parallel parsing.** A reader reads the records one after the other.
- **Not thread-safe.** Use a reader or a writer from one thread at a time. [CsvSerializerOptions](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions) and the
  contexts can be shared.
