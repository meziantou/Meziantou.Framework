---
title: Reading CSV
description: Read records from strings, files and streams, get values by position or name, parse them, and handle separators, quotes, comments, empty lines and spaces.
---

[CsvReader](cref:Meziantou.Framework.Csv.CsvReader) reads UTF-16 text. Create one with [Create](cref:Meziantou.Framework.Csv.CsvReader.Create) (a `TextReader`, a `Stream` or memory),
[CreateFromString](cref:Meziantou.Framework.Csv.CsvReader.CreateFromString) or [CreateFromFile](cref:Meziantou.Framework.Csv.CsvReader.CreateFromFile), then call [Read](cref:Meziantou.Framework.Csv.CsvReader.Read) until it returns `false`:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = CsvReader.CreateFromString("""
    Name,Age
    Alice,30
    Bob,25
    """);

var lines = new List<string>();
while (reader.Read())
{
    var record = reader.Current;
    lines.Add($"{record["Name"].Span} is {record["Age"].Parse<int>()} years old");
}

Assert.Equal(["Alice is 30 years old", "Bob is 25 years old"], lines);
```
<!-- endref -->

[ReadAsync](cref:Meziantou.Framework.Csv.CsvReader.ReadAsync) reads the next record asynchronously, for readers over files and network streams. It only waits when the
buffer of the reader has no complete record left. The reader can also be enumerated with `foreach`.

## Values are valid until the next record

[Current](cref:Meziantou.Framework.Csv.CsvReader.Current) is a [CsvRecord](cref:Meziantou.Framework.Csv.CsvRecord), and each value is a [CsvField](cref:Meziantou.Framework.Csv.CsvField). Both are `ref struct` views of the buffer of the
reader: reading a value does not copy it, and the views are valid until the next call to [Read](cref:Meziantou.Framework.Csv.CsvReader.Read). Use the span of
a value to process it without allocating, and [ToString](cref:Meziantou.Framework.Csv.CsvField.ToString) to keep it:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_ReadSpans;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = CsvReader.CreateFromString("Id,Amount\n1,10.5\n2,20.25");

var total = 0m;
while (reader.Read())
{
    // The values are slices of the buffer of the reader: reading them does not allocate
    ReadOnlySpan<char> amount = reader.Current["Amount"].Span;
    total += decimal.Parse(amount, CultureInfo.InvariantCulture);
}

Assert.Equal(30.75m, total);
```
<!-- endref -->

- [Span](cref:Meziantou.Framework.Csv.CsvField.Span) is the value without its quotes, with doubled quotes unescaped.
- [RawSpan](cref:Meziantou.Framework.Csv.CsvField.RawSpan) is the value as it is in the data, and [IsQuoted](cref:Meziantou.Framework.Csv.CsvField.IsQuoted) says whether it is quoted.
- [ToStringArray](cref:Meziantou.Framework.Csv.CsvRecord.ToStringArray) gives all the values of a record as strings.

## Parse values

[Parse](cref:Meziantou.Framework.Csv.CsvField.Parse) and [TryParse](cref:Meziantou.Framework.Csv.CsvField.TryParse) parse a value as any type that implements `ISpanParsable<T>`, such as numbers, dates
and `Guid`, with [FormatProvider](cref:Meziantou.Framework.Csv.CsvReaderOptions.FormatProvider), the invariant culture by default. [ParseEnum](cref:Meziantou.Framework.Csv.CsvField.ParseEnum) parses the name or the number of a
member of an enumeration:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_ParseValues;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = CsvReader.CreateFromString("Id,Price,Day\n1,9.99,monday\nx,1,sunday");

Assert.True(reader.Read());
Assert.Equal(1, reader.Current["Id"].Parse<int>());
Assert.Equal(9.99m, reader.Current["Price"].Parse<decimal>()); // Parsed with the invariant culture
Assert.Equal(DayOfWeek.Monday, reader.Current["Day"].ParseEnum<DayOfWeek>()); // Names are matched ignoring case

Assert.True(reader.Read());
Assert.False(reader.Current["Id"].TryParse<int>(out _));
var exception = Assert.Throws<CsvException>(() => reader.Current["Id"].Parse<int>());
Assert.Equal(3, exception.LineNumber);
Assert.Equal("Id", exception.ColumnName);
```
<!-- endref -->

An invalid value throws a [CsvException](cref:Meziantou.Framework.Csv.CsvException) that gives the line, the record and the column of the value.

## Rows with a header

[HasHeaderRow](cref:Meziantou.Framework.Csv.CsvReaderOptions.HasHeaderRow) is `true` by default: the first row gives the names of the columns, and it is not returned as a record.
[Header](cref:Meziantou.Framework.Csv.CsvReader.Header) is set once the first record is read:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_ReadWithHeader;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = CsvReader.CreateFromString("""
    Name,Age
    Alice,30
    """);

Assert.True(reader.Read());
var record = reader.Current;

// The header row is read with the first record
Assert.Equal(["Name", "Age"], reader.Header!.ColumnNames);
Assert.Equal(1, reader.Header.IndexOf("Age"));

// Access by column name or by index
Assert.Equal("Alice", record["Name"].ToString());
Assert.Equal("30", record[1].ToString());

// TryGetField returns false for a column the header does not have
Assert.False(record.TryGetField("City", out _));

Assert.False(reader.Read());
```
<!-- endref -->

- Column names are compared with an ordinal, case-sensitive comparison. Set [ColumnNameComparer](cref:Meziantou.Framework.Csv.CsvReaderOptions.ColumnNameComparer) to change it.
- When two columns have the same name, the name gives the first one.
- Reading a column the header does not have throws a `KeyNotFoundException`; reading a value by name without a header
  row throws an `InvalidOperationException`.

## Rows without a header

Set [HasHeaderRow](cref:Meziantou.Framework.Csv.CsvReaderOptions.HasHeaderRow) to `false` to read every row as a record, and read the values by position. An index from the end,
such as `^1`, gives the last value:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_ReadWithoutHeader;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvReaderOptions { HasHeaderRow = false };
using var reader = CsvReader.CreateFromString("""
    Alice,30,New York
    Bob,25,London
    """, options);

var lines = new List<string>();
foreach (var record in reader)
{
    lines.Add($"{record[0].Span}, {record[1].Span}, {record[^1].Span}");
}

Assert.Equal(["Alice, 30, New York", "Bob, 25, London"], lines);
```
<!-- endref -->

## Short records and empty lines

A record can have fewer values than the header row. [TryGetField](cref:Meziantou.Framework.Csv.CsvRecord.TryGetField) returns `false` for a column the record does not reach,
and the indexers throw an `ArgumentOutOfRangeException`. An empty line is a record without columns:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_BlankLinesAndShortRows;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = CsvReader.CreateFromString("Name,Age,City\nAlice,30\n\nBob,25,London");

Assert.True(reader.Read());
var alice = reader.Current;
Assert.Equal(2, alice.ColumnCount);
Assert.False(alice.TryGetField("City", out _)); // The record has no value for this column

Assert.True(reader.Read());
Assert.Equal(0, reader.Current.ColumnCount); // An empty line is a record without columns

Assert.True(reader.Read());
Assert.Equal("London", reader.Current["City"].ToString());
```
<!-- endref -->

Empty values are kept: `a,b,` is a record of three values, the last one empty. A line terminator at the end of the data
does not add a record. Set [ValidateColumnCount](cref:Meziantou.Framework.Csv.CsvReaderOptions.ValidateColumnCount) to throw a [CsvException](cref:Meziantou.Framework.Csv.CsvException) when a record does not have the number of
columns of the header row, or of the first record without a header row.

## Comments and empty lines

[CommentCharacter](cref:Meziantou.Framework.Csv.CsvReaderOptions.CommentCharacter) skips the lines that start with a character, and [SkipEmptyLines](cref:Meziantou.Framework.Csv.CsvReaderOptions.SkipEmptyLines) skips the empty lines:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_CommentsAndEmptyLines;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvReaderOptions
{
    CommentCharacter = '#',
    SkipEmptyLines = true,
};
using var reader = CsvReader.CreateFromString("""
    # Exported on 2026-11-21
    Name,Age

    Alice,30
    # Bob left
    Charlie,35
    """, options);

var names = new List<string>();
foreach (var record in reader)
{
    names.Add(record["Name"].ToString());
}

Assert.Equal(["Alice", "Charlie"], names);
```
<!-- endref -->

A comment character in the middle of a line, or at the start of a line inside a quoted value, is part of the data.

## Separators and quotes

[Separator](cref:Meziantou.Framework.Csv.CsvReaderOptions.Separator) is `,` and [Quote](cref:Meziantou.Framework.Csv.CsvReaderOptions.Quote) is `"` by default. Change them to read other formats, such as tab-separated values:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_CustomSeparatorAndQuote;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvReaderOptions
{
    Separator = '\t',      // Tab-separated
    Quote = '\'',          // Single quote instead of double quote
};
using var reader = CsvReader.CreateFromString("Name\tComment\nAlice\t'Tab\tand ''quote'''", options);

Assert.True(reader.Read());
Assert.Equal("Alice", reader.Current["Name"].ToString());
Assert.Equal("Tab\tand 'quote'", reader.Current["Comment"].ToString());
```
<!-- endref -->

Set [Separator](cref:Meziantou.Framework.Csv.CsvReaderOptions.Separator) to `null` to detect it from the first line: the most frequent of `,`, `;`, tab and `|` outside quoted
values. [Separator](cref:Meziantou.Framework.Csv.CsvReader.Separator) on the reader gives the separator in use:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_DetectSeparator;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvReaderOptions { Separator = null };
using var reader = CsvReader.CreateFromString("Name;Age\nAlice;30", options);

Assert.True(reader.Read());
Assert.Equal(';', reader.Separator);
Assert.Equal("30", reader.Current["Age"].ToString());
```
<!-- endref -->

Set [Quote](cref:Meziantou.Framework.Csv.CsvReaderOptions.Quote) to `null` when the data has no quoted values: every quote character is then part of a value.

## Quoted values

A value that starts with the quote character ends at the next quote character that is not doubled. Inside it, the
separator and line terminators are part of the value, and a doubled quote is one quote:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_QuotedValues;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = CsvReader.CreateFromString(""""
    "Value with, comma","Normal value","Value with ""quotes""","Multi-line
    value"
    """", new CsvReaderOptions { HasHeaderRow = false });

Assert.True(reader.Read());
var record = reader.Current;
Assert.Equal("Value with, comma", record[0].ToString());
Assert.Equal("Normal value", record[1].ToString());
Assert.Equal("Value with \"quotes\"", record[2].ToString());
Assert.Equal("Multi-line\nvalue", record[3].ToString(), ignoreLineEndingDifferences: true);
Assert.True(record[0].IsQuoted);
Assert.Equal("\"Value with, comma\"", record[0].RawSpan.ToString());
```
<!-- endref -->

- A quote character in the middle of a value that is not quoted, as in `a"b`, is part of the value.
- The characters after the closing quote are part of the value: `"a"b` is `ab`.
- A quoted value that is not closed ends at the end of the data.
- The line terminators of a multi-line value are kept as they are in the data.
- Records end at `\n`, `\r\n` or `\r`, so files written on any platform read the same.

Set [Strict](cref:Meziantou.Framework.Csv.CsvReaderOptions.Strict) to throw a [CsvException](cref:Meziantou.Framework.Csv.CsvException) for these cases instead:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_Strict;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvReaderOptions { HasHeaderRow = false, Strict = true };
using var reader = CsvReader.CreateFromString("a,b\"c\nd,e", options);

var exception = Assert.Throws<CsvException>(() => reader.Read());
Assert.Equal(1, exception.LineNumber);
Assert.Equal(1, exception.ColumnIndex);
```
<!-- endref -->

## Spaces around values

[Trim](cref:Meziantou.Framework.Csv.CsvReaderOptions.Trim) removes the spaces around the values ([CsvTrimOptions.Outer](cref:Meziantou.Framework.Csv.CsvTrimOptions.Outer)), at the start and the end of quoted values
([CsvTrimOptions.InsideQuotes](cref:Meziantou.Framework.Csv.CsvTrimOptions.InsideQuotes)), or both. With [CsvTrimOptions.Outer](cref:Meziantou.Framework.Csv.CsvTrimOptions.Outer), a quoted value can be preceded by spaces:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_Trim;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvReaderOptions { HasHeaderRow = false, Trim = CsvTrimOptions.All };
using var reader = CsvReader.CreateFromString("  a  , \" b,c \" ,d", options);

Assert.True(reader.Read());
Assert.Equal(["a", "b,c", "d"], reader.Current.ToStringArray());
```
<!-- endref -->

Only the space character is removed: tabs are part of the values.

## Positions for error messages

[RowIndex](cref:Meziantou.Framework.Csv.CsvReader.RowIndex) is the 0-based index of the current record, without the header row and the comment lines. [LineNumber](cref:Meziantou.Framework.Csv.CsvReader.LineNumber) is
the 1-based line where the record starts, counting the line terminators inside quoted values, and [Position](cref:Meziantou.Framework.Csv.CsvReader.Position) is its offset
from the start of the data:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_Positions;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = CsvReader.CreateFromString("Name,Comment\nAlice,\"Two\nlines\"\nBob,unknown");

var positions = new List<string>();
while (reader.Read())
{
    positions.Add($"record {reader.RowIndex} starts at line {reader.LineNumber}");
}

Assert.Equal(["record 0 starts at line 2", "record 1 starts at line 4"], positions);
```
<!-- endref -->

## UTF-8 data

[Utf8CsvReader](cref:Meziantou.Framework.Csv.Utf8CsvReader) reads UTF-8 bytes without decoding them. It has the same options and members as [CsvReader](cref:Meziantou.Framework.Csv.CsvReader); its values are
[Utf8CsvField](cref:Meziantou.Framework.Csv.Utf8CsvField), whose [Parse](cref:Meziantou.Framework.Csv.Utf8CsvField.Parse) parses the bytes directly for the types that implement `IUtf8SpanParsable<T>`, such
as numbers:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=Utf8CsvReader_Read;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream("Name,Age\nAlice,30\nBob,25"u8.ToArray());
using var reader = Utf8CsvReader.Create(stream);

var totalAge = 0;
var names = new List<string>();
foreach (var record in reader)
{
    names.Add(record["Name"].ToString());
    totalAge += record["Age"].Parse<int>(); // Parses the UTF-8 bytes, without decoding them
}

Assert.Equal(["Alice", "Bob"], names);
Assert.Equal(55, totalAge);
```
<!-- endref -->

- [ParseUtf16](cref:Meziantou.Framework.Csv.Utf8CsvField.ParseUtf16) decodes the value first, for the types that only parse UTF-16 text, such as `DateTime`.
- The separator, the quote and the comment characters must be ASCII characters.
- A UTF-8 byte order mark at the start of the data is skipped. Invalid UTF-8 sequences are replaced with U+FFFD when a
  value is converted to a string.

[CsvReader.Create](cref:Meziantou.Framework.Csv.CsvReader.Create) also accepts a `Stream`, which it decodes with UTF-8 by default, or with the encoding of its byte order
mark. Prefer [Utf8CsvReader](cref:Meziantou.Framework.Csv.Utf8CsvReader) for UTF-8 files: it skips the decoding of the values you do not read.

## Long rows and untrusted data

The buffer of the reader grows when a row does not fit. [MaxRowLength](cref:Meziantou.Framework.Csv.CsvReaderOptions.MaxRowLength) limits the length of a row read from a `TextReader` or a
`Stream`, 16 777 216 characters or bytes by default: a longer row throws a [CsvException](cref:Meziantou.Framework.Csv.CsvException). This bounds the memory a
malformed file uses, such as a quote that is never closed.
