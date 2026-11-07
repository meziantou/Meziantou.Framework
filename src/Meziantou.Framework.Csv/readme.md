# Meziantou.Framework.Csv

A lightweight and efficient .NET library for reading and writing CSV files with support for quoted values, custom separators, and header rows.

## Features

- **Async/await support** - All I/O operations are asynchronous
- **Flexible parsing** - Configurable separator and quote characters
- **Header row support** - Parse CSV files with or without headers
- **Quoted values** - Handles multi-line quoted values and escaped quotes

## Usage

The types are in the `Meziantou.Framework.Csv` namespace. `CsvReader` reads from any `TextReader` and `CsvWriter`
writes to any `TextWriter`, such as a `StreamReader` or a `StreamWriter` over a file. The examples below use in-memory strings so you can see the input and the output.

### Reading CSV Files

#### Reading without headers

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

#### Reading with headers

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

#### Using custom separators and quotes

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

### Writing CSV Files

#### Writing simple rows

Values that contain the separator, the quote character, or a line break are quoted.

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

#### Writing with custom separators

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

#### Writing row values incrementally

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

#### Disabling quotes

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

## Special Cases

### Quoted Values

The library properly handles:
- Multi-line values within quotes
- Escaped quotes (doubled quotes: `""`)
- Values containing separators
- Quotes at the beginning, middle, or end of values

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
