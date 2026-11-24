---
title: Performance
description: How the reader and the writer work, and the choices that keep reading and writing fast and free of allocations.
---

## How the reader works

The reader loads the data into a buffer and scans it in batches of records. It compares blocks of 16, 32 or 64
characters or bytes at a time with the separator, the quote and the line terminators, using the widest SIMD instructions
of the processor (AVX-512, AVX2, SSE or NEON; on Arm64, a block of 64 is four NEON vectors), and stores where each
record starts and where each of its columns ends.
Each character is compared once, and the separators are found from the result of the comparison without reading the
data again. The quotes of a block are resolved at once too: the characters after an odd number of quotes are in a quoted
value, so its separators are skipped without reading them one by one. A comment line, and a record whose quotes do more
than open a value, close it or escape a quote, that has a line break in a quoted value, or whose values are trimmed, go
through a slower path that reads each quote and line break on its own; the next records go back to the faster one.
Reading a record then only takes the next entry of the batch, and reading a value only slices the buffer.
The scan also notes the first column of each record with a doubled quote, or with another quote that does not only open
or close its value: the quoted values of the columns before it are slices of the buffer too, between their quotes.
A quoted value that contains doubled quotes is unescaped the first time it is read, into a buffer reused for every
record. When a record does not fit, the buffer grows and the scan continues where it stopped.

With [Strict](cref:Meziantou.Framework.Csv.CsvReaderOptions.Strict), an invalid record still throws when it is read: the records before it are returned first.

## Reading

- **Read by name with the same strings.** The header remembers the string of each column name it found: reading the columns of each record
  with the same strings, such as literals, is almost as fast as reading them by index, whatever the order. A new string for each value is
  compared with the names of the header again.
- **Use the spans.** [Span](cref:Meziantou.Framework.Csv.CsvField.Span), [Parse](cref:Meziantou.Framework.Csv.CsvField.Parse) and [TryParse](cref:Meziantou.Framework.Csv.CsvField.TryParse) do not allocate. Call [ToString](cref:Meziantou.Framework.Csv.CsvField.ToString) only for the values you keep.
- **Parse floating-point numbers with the invariant culture.** With the default [FormatProvider](cref:Meziantou.Framework.Csv.CsvReaderOptions.FormatProvider),
  [Parse](cref:Meziantou.Framework.Csv.CsvField.Parse) and [TryParse](cref:Meziantou.Framework.Csv.CsvField.TryParse) read a `double`, a `float` or a `Half` with few digits, such as `-123.45` or `1.5e-3`,
  through a faster path, with the same value. A `double` or a `float` with up to 19 digits, such as its round-trip format
  (`0.30000000000000004`), also has a faster path when its exponent is from about -48 to 80: two to three times as fast as `double.Parse`
  and `float.Parse`. The other values are left to the parsers of the BCL.
- **Read UTF-8 files with [Utf8CsvReader](cref:Meziantou.Framework.Csv.Utf8CsvReader).** It does not decode the data, and parses numbers from the bytes.
  The scan also notes the records whose bytes are all ASCII: [ToString](cref:Meziantou.Framework.Csv.Utf8CsvField.ToString), and the deserialization of UTF-8 data into
  `string` members with the default converter, widen their values into strings without checking them again.
- **Pass memory when you already have it.** [CsvReader.Create](cref:Meziantou.Framework.Csv.CsvReader.Create) with a `ReadOnlyMemory<char>`, [CreateFromString](cref:Meziantou.Framework.Csv.CsvReader.CreateFromString) and
  [Utf8CsvReader.Create](cref:Meziantou.Framework.Csv.Utf8CsvReader.Create) with a `ReadOnlyMemory<byte>` read the data in place, without a copy.
- **Pool repeated strings.** When the data repeats short values, such as categories or country codes,
  [PoolStrings](cref:Meziantou.Framework.Csv.CsvReaderOptions.PoolStrings) reuses the strings instead of allocating them again.
- **Keep the default buffer size.** A larger [BufferSize](cref:Meziantou.Framework.Csv.CsvReaderOptions.BufferSize) rarely helps: the buffer grows when a record needs it.
- **Use a separator and a quote from U+0001 to U+00FE for UTF-16 data.** The SIMD path narrows each character to a byte;
  other characters use a slower path.

## Writing

- **Write formattable values directly.** [WriteField](cref:Meziantou.Framework.Csv.CsvWriter.WriteField) formats numbers and dates into the buffer of the writer, without a string.
  Decimals in the default format of the invariant culture are written by a faster path than `decimal.TryFormat`, which writes the same text.
  So are doubles with up to 9 decimals from 0.0001 to 10^9, such as `-12.3`, about four times as fast as `double.TryFormat`; the serializer
  writes them the same way.
  Integers and decimals in the default format of the invariant culture, and dates in the round-trip format (`O`), are not
  searched for characters that need quotes.
- **Write UTF-8 with [Utf8CsvWriter](cref:Meziantou.Framework.Csv.Utf8CsvWriter)** when the output is a file, a stream or a network response.
- **Keep a new line made of `\r` and `\n`.** Text values are then checked for characters that need quotes while they are
  copied, or converted to UTF-8 when they are ASCII, in blocks of 8 or 16 characters. A [NewLine](cref:Meziantou.Framework.Csv.CsvWriterOptions.NewLine) with
  other characters makes the writer search each value before copying it.
- **Use [EndRowAsync](cref:Meziantou.Framework.Csv.CsvWriter.EndRowAsync)** with an asynchronous output: the writer only awaits when its buffer is mostly full.

## Serialization

- **Reuse the options or the context.** They cache the metadata of the types; creating new options for each call
  discovers the types again.
- **Use a generated context.** It skips the discovery of the types with reflection, and works with Native AOT.
- **Constructors and records are fast too.** The values of constructor parameters are stored in typed slots, reused for
  every record, so they are not boxed.
- **Keep the default formats.** Dates and times in the ISO 8601 round-trip format (`O`), which the serializer writes,
  and decimals written as plain numbers, such as `-123.45`, with the invariant culture, are parsed and written by a
  faster path than the other formats. `int` and `long` values written as plain numbers, such as `42` or `-7`, and
  doubles, floats and `Half` values with few digits, are parsed by a faster path too. The numbers and the dates written
  in the default formats with the invariant culture are not searched for characters that need quotes, unless the
  separator, the quote or the new line is one of their characters.
- **Serialize to a string only when you need one.** The text is kept in chunks and copied once, into the string;
  writing to a stream, a `TextWriter` or an `IBufferWriter<T>` does not keep the whole text in memory.
- **Convert large files on several threads.** With [MaxDegreeOfParallelism](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.MaxDegreeOfParallelism), the records are converted on the
  thread pool while the calling thread splits the data into records. See [Convert the records in parallel](05-Serialization.md#convert-the-records-in-parallel).
- **Stream large files** with [DeserializeEnumerable](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.DeserializeEnumerable) or [DeserializeAsyncEnumerable](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.DeserializeAsyncEnumerable) instead of loading every record.
- **Loading every record is cheap on the garbage collector.** [Deserialize](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.Deserialize) and [DeserializeAsync](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.DeserializeAsync)
  collect the records in small arrays, then copy them once to a list of the exact size. A list that grows while the records
  are read puts its larger arrays on the large object heap, which starts full collections of every record read so far.
  Most of the remaining time of a file with text values is the allocation of the strings and the records.

## Benchmarks

The `benchmarks/Meziantou.Framework.Csv.Benchmarks` project of the repository measures the reader (UTF-16 and UTF-8, spans and
strings), the writer (strings and formatted values), the serializer (generated metadata and reflection) and the converters on your machine:

```shell
dotnet run -c Release --project benchmarks/Meziantou.Framework.Csv.Benchmarks -- --filter "*"
```

The `SepReadComparisonBenchmark` and `SepWriteComparisonBenchmark` classes read and write the same data with
[Sep](https://github.com/nietras/Sep), with values that are never quoted, sometimes quoted, or always quoted:

```shell
dotnet run -c Release --project benchmarks/Meziantou.Framework.Csv.Benchmarks -- --filter "*Sep*"
```
