---
title: Reading CSV
description: Read rows with or without a header row, get values by position or name, change the separator and quote, and handle short rows.
---

[CsvReader.ReadRowAsync](cref:Meziantou.Framework.Csv.CsvReader.ReadRowAsync) reads the next row and returns `null` at the end of the data. Read the rows in a loop:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_ReadRowAsync;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = new StringReader("Name,Age\nJohn,30\nJane,25");
var csvReader = new CsvReader(reader) { HasHeaderRow = true };

var lines = new List<string>();
while (await csvReader.ReadRowAsync() is { } row)
{
    lines.Add($"{row["Name"]} is {row["Age"]} years old");
}

Assert.Equal(["John is 30 years old", "Jane is 25 years old"], lines);
```
<!-- endref -->

Test the `null` returned by [ReadRowAsync](cref:Meziantou.Framework.Csv.CsvReader.ReadRowAsync) to find the end of the data rather than [EndOfStream](cref:Meziantou.Framework.Csv.CsvReader.EndOfStream): when its buffer is
empty, [EndOfStream](cref:Meziantou.Framework.Csv.CsvReader.EndOfStream) asks the underlying `TextReader` synchronously, so it can block.

## Rows without a header

[HasHeaderRow](cref:Meziantou.Framework.Csv.CsvReader.HasHeaderRow) is `false` by default: every row is data, and values are read by position:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_ReadWithoutHeader;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = new StringReader("""
    Alice,30,New York
    Bob,25,London
    """);
var csvReader = new CsvReader(reader)
{
    HasHeaderRow = false,
};

var lines = new List<string>();
while (await csvReader.ReadRowAsync() is { } row)
{
    lines.Add($"{row[0]}, {row[1]}, {row[2]}");
}

Assert.Equal(["Alice, 30, New York", "Bob, 25, London"], lines);
```
<!-- endref -->

## Rows with a header

Set [HasHeaderRow](cref:Meziantou.Framework.Csv.CsvReader.HasHeaderRow) to `true` to read the names of the columns from the first row. [ReadRowAsync](cref:Meziantou.Framework.Csv.CsvReader.ReadRowAsync) does not return that
row. A value can then be read by position, by column name or by [CsvColumn](cref:Meziantou.Framework.Csv.CsvColumn):

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_ReadWithHeader;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = new StringReader("""
    Name,Age
    Alice,30
    """);
var csvReader = new CsvReader(reader)
{
    HasHeaderRow = true,
};

var row = await csvReader.ReadRowAsync();
Assert.NotNull(row);

// Access by column name
Assert.Equal("Alice", row["Name"]);
Assert.Equal("30", row["Age"]);

// Access by index
Assert.Equal("Alice", row[0]);

// Access by CsvColumn
var nameColumn = row.Columns?.FirstOrDefault(c => c.Name == "Name");
Assert.NotNull(nameColumn);
Assert.Equal("Alice", row[nameColumn]);

// ReadRowAsync returns null at the end of the data
Assert.Null(await csvReader.ReadRowAsync());
```
<!-- endref -->

- [Columns](cref:Meziantou.Framework.Csv.CsvRow.Columns) lists the columns, with their [Name](cref:Meziantou.Framework.Csv.CsvColumn.Name) and [Index](cref:Meziantou.Framework.Csv.CsvColumn.Index). It is `null` when there is no header row.
- Column names are compared with an ordinal, case-sensitive comparison. When two columns have the same name, the name
  gives the value of the first one.
- Reading a value by name throws an `InvalidOperationException` when the reader has no header row.

[Values](cref:Meziantou.Framework.Csv.CsvRow.Values) holds the values of the row, in order.

## Short rows and blank lines

A row can have fewer values than the header row. Reading a column the row does not reach returns `null`, as does
reading a column the header row does not have. A blank line is a row without values:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_BlankLinesAndShortRows;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = new StringReader("Name,Age,City\nAlice,30\n\nBob,25,London");
var csvReader = new CsvReader(reader) { HasHeaderRow = true };

var alice = await csvReader.ReadRowAsync();
Assert.NotNull(alice);
Assert.Equal(["Alice", "30"], alice.Values);
Assert.Null(alice["City"]);    // The row has no value for this column
Assert.Null(alice["Country"]); // The header row has no such column

var blankLine = await csvReader.ReadRowAsync();
Assert.NotNull(blankLine);
Assert.Empty(blankLine.Values);

var bob = await csvReader.ReadRowAsync();
Assert.NotNull(bob);
Assert.Equal("London", bob["City"]);
```
<!-- endref -->

Reading by position is strict: `row[index]` throws an `ArgumentOutOfRangeException` when the row has no value at that
position. Check `row.Values.Count` first when rows can be short.

Empty values are kept: `a,b,` is a row of three values, the last one empty. A line terminator at the end of the data
does not add an empty row.

## A row as a dictionary

[CsvRow](cref:Meziantou.Framework.Csv.CsvRow) implements `IReadOnlyDictionary<string, string?>`, keyed by column name. Pass a row to code that expects a
dictionary:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvRow_AsDictionary;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = new StringReader("Name,Age\nAlice,30");
var csvReader = new CsvReader(reader) { HasHeaderRow = true };
var row = await csvReader.ReadRowAsync();
Assert.NotNull(row);

IReadOnlyDictionary<string, string?> values = row;
Assert.Equal(["Name", "Age"], values.Keys);
Assert.True(values.TryGetValue("Age", out var age));
Assert.Equal("30", age);
```
<!-- endref -->

The dictionary has one entry per column of the header row, even when the row is short: the missing values are `null`.

## Separators and quotes

[Separator](cref:Meziantou.Framework.Csv.CsvReader.Separator) is `,` and [Quote](cref:Meziantou.Framework.Csv.CsvReader.Quote) is `"` by default. Change them to read other formats, such as tab-separated values:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_CustomSeparatorAndQuote;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = new StringReader("Name\tComment\nAlice\t'Tab\tand ''quote'''");
var csvReader = new CsvReader(reader)
{
    Separator = '\t',      // Tab-separated
    Quote = '\'',          // Single quote instead of double quote
    HasHeaderRow = true,
};

var row = await csvReader.ReadRowAsync();
Assert.NotNull(row);
Assert.Equal("Alice", row["Name"]);
Assert.Equal("Tab\tand 'quote'", row["Comment"]);
```
<!-- endref -->

Set [Quote](cref:Meziantou.Framework.Csv.CsvReader.Quote) to `null` when the data has no quoted values: every quote character is then part of a value.

## Quoted values

A value that starts with the quote character ends at the next quote character that is not doubled. Inside it, the
separator and line breaks are part of the value, and a doubled quote is one quote:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_QuotedValues;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = new StringReader(""""
    "Value with, comma","Normal value","Value with ""quotes""","Multi-line
    value"
    """");
var csvReader = new CsvReader(reader);

var row = await csvReader.ReadRowAsync();
Assert.NotNull(row);
Assert.Equal("Value with, comma", row[0]);
Assert.Equal("Normal value", row[1]);
Assert.Equal("Value with \"quotes\"", row[2]);
Assert.Equal("Multi-line\nvalue", row[3], ignoreLineEndingDifferences: true);
```
<!-- endref -->

- A quote character in the middle of an unquoted value, as in `a"b`, is part of the value.
- The line breaks of a multi-line value are kept as they are in the data: `\r\n` stays `\r\n`.

Rows end at `\n`, `\r\n` or `\r`, so files written on any platform read the same.

## Positions for error messages

The reader counts what it reads, which helps report where bad data is:

| Property | Value |
| --- | --- |
| [RowNumber](cref:Meziantou.Framework.Csv.CsvReader.RowNumber) | The 0-based index of the row just returned. The header row counts: the first data row after a header is row 1. |
| [LineNumber](cref:Meziantou.Framework.Csv.CsvReader.LineNumber) | The number of line terminators read so far, including those inside quoted values: the physical line in the input. |
| [ColumnNumber](cref:Meziantou.Framework.Csv.CsvReader.ColumnNumber) | The number of characters read since the start of the current row. It is a character position, not a column index. |

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_RowNumber;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = new StringReader("Name,Age\nAlice,30\nBob,unknown");
var csvReader = new CsvReader(reader) { HasHeaderRow = true };

var errors = new List<string>();
while (await csvReader.ReadRowAsync() is { } row)
{
    if (!int.TryParse(row["Age"], CultureInfo.InvariantCulture, out _))
    {
        errors.Add($"Row {csvReader.RowNumber}: invalid age '{row["Age"]}'");
    }
}

// The header row is row 0
Assert.Equal(["Row 2: invalid age 'unknown'"], errors);
```
<!-- endref -->

## Custom rows and columns

[CsvReader](cref:Meziantou.Framework.Csv.CsvReader) calls the protected virtual methods [CreateColumn](cref:Meziantou.Framework.Csv.CsvReader.CreateColumn) and [CreateRow](cref:Meziantou.Framework.Csv.CsvReader.CreateRow) to create each column of the header and
each row. Override them in a derived class to return your own types derived from [CsvColumn](cref:Meziantou.Framework.Csv.CsvColumn) and [CsvRow](cref:Meziantou.Framework.Csv.CsvRow), for
instance a row with typed properties.
