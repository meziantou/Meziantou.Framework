---
title: Writing CSV
description: Write rows at once or value by value, with automatic quoting, a custom separator and a custom line terminator.
---

`CsvWriter.WriteRowAsync` writes one row. Values that contain the separator, the quote character or a line break are
quoted:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_WriteRows;format=md-fenced-code-block;dedent=true -->
```csharp
using var writer = new StringWriter();
var csvWriter = new CsvWriter(writer)
{
    EndOfLine = "\n", // Environment.NewLine by default
};

await csvWriter.WriteRowAsync("Name", "Age", "City");
await csvWriter.WriteRowAsync("Alice", "30", "New York");
await csvWriter.WriteRowAsync("Bob", "25", "London, UK");

Assert.Equal("Name,Age,City\nAlice,30,New York\nBob,25,\"London, UK\"", writer.ToString());
```
<!-- endref -->

There is no header option: the header is the first row you write.

## Quoting

The writer quotes a value when it contains the quote character, the separator, `\r`, `\n`, or a character of
`EndOfLine`. Inside a quoted value, each quote character is doubled. Other values are written as they are. `null` and
the empty string are both written as nothing:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_Quoting;format=md-fenced-code-block;dedent=true -->
```csharp
using var writer = new StringWriter();
var csvWriter = new CsvWriter(writer) { EndOfLine = "\n" };

await csvWriter.WriteRowAsync("plain", "with,comma", "with \"quotes\"", "two\nlines", null, "");

Assert.Equal(""""
    plain,"with,comma","with ""quotes""","two
    lines",,
    """", writer.ToString(), ignoreLineEndingDifferences: true);
```
<!-- endref -->

`CsvReader` reads these values back as they were written, except `null`, which is read back as an empty string.

## Separators

`Separator` is `,` by default. Use `;`, `\t` or any other character:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_CustomSeparator;format=md-fenced-code-block;dedent=true -->
```csharp
using var writer = new StringWriter();
var csvWriter = new CsvWriter(writer)
{
    Separator = ';',
    EndOfLine = "\n",
};

await csvWriter.WriteRowAsync("Column1", "Column2");
await csvWriter.WriteRowAsync("Value1", "Value2");

Assert.Equal("Column1;Column2\nValue1;Value2", writer.ToString());
```
<!-- endref -->

Read the data with the same `Separator` and `Quote` as it was written with.

## Line terminators

`EndOfLine` is written between rows, and is `Environment.NewLine` by default, so the output depends on the operating
system. Set it to produce the same output everywhere, for instance `"\r\n"` as RFC 4180 asks, or `"\n"`. The writer
does not write a line terminator after the last row.

## Write a row value by value

When the values of a row come one at a time, call `BeginRowAsync`, then `WriteValueAsync` for each value, or
`WriteValuesAsync` for several:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_WriteValuesIncrementally;format=md-fenced-code-block;dedent=true -->
```csharp
using var writer = new StringWriter();
var csvWriter = new CsvWriter(writer)
{
    EndOfLine = "\n",
};

await csvWriter.BeginRowAsync();
await csvWriter.WriteValueAsync("First");
await csvWriter.WriteValueAsync("Second");
await csvWriter.WriteValueAsync("Third");

await csvWriter.BeginRowAsync();
await csvWriter.WriteValuesAsync("Fourth", "Fifth", "Sixth");

Assert.Equal("First,Second,Third\nFourth,Fifth,Sixth", writer.ToString());
```
<!-- endref -->

`BeginRowAsync` writes the line terminator that ends the previous row. `WriteRowAsync` is `BeginRowAsync` followed by
`WriteValuesAsync`. Both also accept an `IEnumerable<string?>`.

## Disable quoting

Set `Quote` to `null` to write every value as it is, without quoting or escaping:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_DisableQuotes;format=md-fenced-code-block;dedent=true -->
```csharp
using var writer = new StringWriter();
var csvWriter = new CsvWriter(writer)
{
    Quote = null, // Disable quoting (values won't be escaped)
};

await csvWriter.WriteRowAsync("A\"", "B");

Assert.Equal("A\",B", writer.ToString());
```
<!-- endref -->

The output is then only valid when no value contains the separator or a line break: the writer does not check.

## Flushing

The writer writes to the `TextWriter` as you go and never flushes or disposes it. Dispose the `TextWriter`, or call its
`FlushAsync`, once the last row is written; see [Write a file](02-GettingStarted.md#write-a-file).
