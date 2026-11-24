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
- **No record filter in the serializer.** [DeserializationErrorHandler](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.DeserializationErrorHandler) skips the records that cannot be
  converted, but the serializer cannot skip a valid record by its values before converting it. Read such data with
  [CsvReader](cref:Meziantou.Framework.Csv.CsvReader), or filter the objects after reading them.
- **No line repair.** A bad data handler can accept or skip a malformed record, not rewrite it: a quoted value that is
  never closed takes the rest of the data, and skipping it skips the rest of the data.
- **Not thread-safe.** Use a reader or a writer from one thread at a time. [CsvSerializerOptions](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions) and the
  contexts can be shared.

## Differences with other parsers

The reader is tested against the csv-spectrum files and the parser tests of Papa Parse. It reads them the same way,
except where it follows RFC 4180 or keeps the data as it is:

- **A final line terminator ends the last record.** `a,b\n` is one record; Papa Parse returns an empty record after it.
- **Duplicate column names are kept.** The header of `A,A` is `A` and `A`; Papa Parse renames the second one `A_1`.
- **The characters after a closing quote are part of the value.** `"a" ,b` reads `a ` as the first value; Papa Parse
  ignores the spaces before the separator. Set [Trim](cref:Meziantou.Framework.Csv.CsvReaderOptions.Trim) to [CsvTrimOptions.Outer](cref:Meziantou.Framework.Csv.CsvTrimOptions.Outer) to remove them.
- **A quote closes a quoted value even when a separator does not follow it.** `"a"b,c` is two values, `ab` and `c`;
  Papa Parse reads the quote as a character and continues the value. [Strict](cref:Meziantou.Framework.Csv.CsvReaderOptions.Strict) rejects such data, and
  [BadDataHandler](cref:Meziantou.Framework.Csv.CsvReaderOptions.BadDataHandler) reports it.
- **Every line terminator ends a record.** `\n`, `\r\n` and `\r` can be mixed in the same data; Papa Parse uses the first
  one it finds and reads the others as characters.
- **A quote is escaped by doubling it.** Another escape character, such as `\"`, is not supported.
