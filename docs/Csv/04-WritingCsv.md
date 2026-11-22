---
title: Writing CSV
description: Write rows value by value or at once, format numbers without allocating, control quoting, protect against CSV injection, and write asynchronously.
---

[CsvWriter](cref:Meziantou.Framework.Csv.CsvWriter) writes UTF-16 text to a `TextWriter`, a `Stream`, an `IBufferWriter<char>`, a `StringBuilder` or a file.
[WriteRow](cref:Meziantou.Framework.Csv.CsvWriter.WriteRow) writes a row at once; [WriteField](cref:Meziantou.Framework.Csv.CsvWriter.WriteField) writes a value and [EndRow](cref:Meziantou.Framework.Csv.CsvWriter.EndRow) ends the row:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_Write;format=md-fenced-code-block;dedent=true -->
```csharp
var output = new StringBuilder();
using (var writer = CsvWriter.Create(output, new CsvWriterOptions { NewLine = "\n" }))
{
    writer.WriteRow("Name", "Age", "City");
    writer.WriteRow("Alice", "30", "New York");

    // Write a row value by value: numbers are formatted in the buffer, with the invariant culture
    writer.WriteField("Bob");
    writer.WriteField(25);
    writer.WriteField("London, UK"); // Quoted: the value contains the separator
    writer.EndRow();
} // Disposing the writer flushes the data

Assert.Equal("Name,Age,City\nAlice,30,New York\nBob,25,\"London, UK\"\n", output.ToString());
```
<!-- endref -->

- Every row ends with [NewLine](cref:Meziantou.Framework.Csv.CsvWriterOptions.NewLine), `\r\n` by default as in RFC 4180, including the last one.
- `null` is written as an empty value.
- [WriteField](cref:Meziantou.Framework.Csv.CsvWriter.WriteField) formats the values that implement `ISpanFormattable`, such as numbers and dates, directly in the buffer of the
  writer, with [FormatProvider](cref:Meziantou.Framework.Csv.CsvWriterOptions.FormatProvider), the invariant culture by default. Pass a format string to change the format.
- The writer does not own the output it does not create: pass `leaveOpen: true` to keep a `TextWriter` or a `Stream`
  open when the writer is disposed.

## Quoting

A value is quoted when it contains the separator, the quote character, a line terminator or a character of
[NewLine](cref:Meziantou.Framework.Csv.CsvWriterOptions.NewLine). Quotes inside a quoted value are doubled:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_Quoting;format=md-fenced-code-block;dedent=true -->
```csharp
var output = new StringBuilder();
using (var writer = CsvWriter.Create(output, new CsvWriterOptions { NewLine = "\n" }))
{
    writer.WriteRow("comma, here", "quote \" here", "line\nbreak", "plain");
}

Assert.Equal("\"comma, here\",\"quote \"\" here\",\"line\nbreak\",plain\n", output.ToString());
```
<!-- endref -->

[QuoteMode](cref:Meziantou.Framework.Csv.CsvWriterOptions.QuoteMode) quotes every value ([CsvQuoteMode.Always](cref:Meziantou.Framework.Csv.CsvQuoteMode.Always)), or none ([CsvQuoteMode.Never](cref:Meziantou.Framework.Csv.CsvQuoteMode.Never)):

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_QuoteModes;format=md-fenced-code-block;dedent=true -->
```csharp
var always = new StringBuilder();
using (var writer = CsvWriter.Create(always, new CsvWriterOptions { QuoteMode = CsvQuoteMode.Always, NewLine = "\n" }))
{
    writer.WriteRow("a", "b");
}

var never = new StringBuilder();
using (var writer = CsvWriter.Create(never, new CsvWriterOptions { QuoteMode = CsvQuoteMode.Never, NewLine = "\n" }))
{
    writer.WriteRow("a,b", "c");
}

Assert.Equal("\"a\",\"b\"\n", always.ToString());
Assert.Equal("a,b,c\n", never.ToString()); // Never: the value is written as it is
```
<!-- endref -->

With [CsvQuoteMode.Never](cref:Meziantou.Framework.Csv.CsvQuoteMode.Never), or when [Quote](cref:Meziantou.Framework.Csv.CsvWriterOptions.Quote) is `null`, a value that contains the separator or a line terminator does
not read back as it was written. A row with a single empty value is written as `""` so that it does not read back as an
empty line.

## Separators

[Separator](cref:Meziantou.Framework.Csv.CsvWriterOptions.Separator) is `,` by default. Formatting with the invariant culture keeps the decimal separator distinct from a `,` or
`;` separator; a value that contains the separator is quoted anyway:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_CustomSeparator;format=md-fenced-code-block;dedent=true -->
```csharp
var output = new StringBuilder();
using (var writer = CsvWriter.Create(output, new CsvWriterOptions { Separator = ';', NewLine = "\n" }))
{
    writer.WriteRow("Column1", "Column2");
    writer.WriteField(1.5); // The invariant culture writes a dot, not the separator
    writer.WriteField("a;b");
    writer.EndRow();
}

Assert.Equal("Column1;Column2\n1.5;\"a;b\"\n", output.ToString());
```
<!-- endref -->

## CSV injection

A spreadsheet runs a value that starts with `=`, `+`, `-` or `@` as a formula. When the values come from users, set
[EscapeFormulas](cref:Meziantou.Framework.Csv.CsvWriterOptions.EscapeFormulas) to prefix these text values with a single quote:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_EscapeFormulas;format=md-fenced-code-block;dedent=true -->
```csharp
var output = new StringBuilder();
using (var writer = CsvWriter.Create(output, new CsvWriterOptions { EscapeFormulas = true, NewLine = "\n" }))
{
    writer.WriteField("=HYPERLINK(\"http://example.com\")");
    writer.WriteField(-5); // Numbers are not escaped
    writer.EndRow();
}

Assert.Equal("\"'=HYPERLINK(\"\"http://example.com\"\")\",-5\n", output.ToString());
```
<!-- endref -->

The values written from numbers and other formattable values are not escaped, so negative numbers stay numbers.

## Buffering and asynchronous writing

The writer keeps the data in a buffer of [BufferSize](cref:Meziantou.Framework.Csv.CsvWriterOptions.BufferSize) characters or bytes. [EndRow](cref:Meziantou.Framework.Csv.CsvWriter.EndRow) sends the buffer to the
output when it is mostly full; the `WriteField` methods never do, so a row is never split between two writes.
[EndRowAsync](cref:Meziantou.Framework.Csv.CsvWriter.EndRowAsync) sends it asynchronously: a writer used with the asynchronous methods never blocks on I/O.

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_WriteAsync;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await using (var writer = Utf8CsvWriter.Create(stream, leaveOpen: true))
{
    for (var i = 0; i < 1000; i++)
    {
        writer.WriteField(i);
        writer.WriteField("value");
        await writer.EndRowAsync(); // Sends the buffer to the stream asynchronously when it is mostly full
    }
}

Assert.HasCount(1000, Encoding.UTF8.GetString(stream.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
```
<!-- endref -->

Call [Flush](cref:Meziantou.Framework.Csv.CsvWriter.Flush) or [FlushAsync](cref:Meziantou.Framework.Csv.CsvWriter.FlushAsync), or dispose the writer, to send the rest of the data.

## UTF-8 data

[Utf8CsvWriter](cref:Meziantou.Framework.Csv.Utf8CsvWriter) writes UTF-8 bytes to a `Stream` or an `IBufferWriter<byte>`, without a byte order mark. It formats values
with `IUtf8SpanFormattable`, and writes UTF-8 values as they are:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=Utf8CsvWriter_Write;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
using (var writer = Utf8CsvWriter.Create(stream, leaveOpen: true))
{
    writer.WriteRow("Product", "Price");
    writer.WriteField("Café"u8); // UTF-8 bytes are written as they are
    writer.WriteField(3.5m, "F2");
    writer.EndRow();
}

Assert.Equal("Product,Price\r\nCafé,3.50\r\n", Encoding.UTF8.GetString(stream.ToArray()));
```
<!-- endref -->

The separator and the quote characters must be ASCII characters. [CsvWriter.Create](cref:Meziantou.Framework.Csv.CsvWriter.Create) also accepts a `Stream` and an
encoding, for other encodings.
