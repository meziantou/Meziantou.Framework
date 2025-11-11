# Meziantou.Framework.Csv

A lightweight and efficient .NET library for reading and writing CSV files with support for quoted values, custom separators, and header rows.

## Features

- **Async/await support** - All I/O operations are asynchronous
- **Flexible parsing** - Configurable separator and quote characters
- **Header row support** - Parse CSV files with or without headers
- **Quoted values** - Handles multi-line quoted values and escaped quotes

## Usage

### Reading CSV Files

#### Reading without headers

```csharp
using Meziantou.Framework.Csv;

using var reader = new StreamReader("data.csv");
var csvReader = new CsvReader(reader)
{
    HasHeaderRow = false
};

while (await csvReader.ReadRowAsync() is { } row)
{
    Console.WriteLine($"{row[0]}, {row[1]}, {row[2]}");
}
```

#### Reading with headers

```csharp
using var reader = new StreamReader("data.csv");
var csvReader = new CsvReader(reader)
{
    HasHeaderRow = true
};

while (await csvReader.ReadRowAsync() is { } row)
{
    // Access by column name
    Console.WriteLine($"Name: {row["Name"]}, Age: {row["Age"]}");

    // Access by index
    Console.WriteLine($"First column: {row[0]}");

    // Access by CsvColumn
    var nameColumn = row.Columns?.FirstOrDefault(c => c.Name == "Name");
    if (nameColumn != null)
    {
        Console.WriteLine($"Name: {row[nameColumn]}");
    }
}
```

#### Using custom separators and quotes

```csharp
using var reader = new StreamReader("data.tsv");
var csvReader = new CsvReader(reader)
{
    Separator = '\t',      // Tab-separated
    Quote = '\'',          // Single quote instead of double quote
    HasHeaderRow = true
};

while (await csvReader.ReadRowAsync() is { } row)
{
    // Process rows...
}
```

### Writing CSV Files

#### Writing simple rows

```csharp
using Meziantou.Framework.Csv;

using var writer = new StreamWriter("output.csv");
var csvWriter = new CsvWriter(writer);

// Write multiple rows at once
await csvWriter.WriteRowAsync("Name", "Age", "City");
await csvWriter.WriteRowAsync("Alice", "30", "New York");
await csvWriter.WriteRowAsync("Bob", "25", "London");
```

#### Writing with custom separators

```csharp
using var writer = new StreamWriter("output.csv");
var csvWriter = new CsvWriter(writer)
{
    Separator = ';',
    EndOfLine = "\n"
};

await csvWriter.WriteRowAsync("Column1", "Column2");
await csvWriter.WriteRowAsync("Value1", "Value2");
```

#### Writing row values incrementally

```csharp
using var writer = new StreamWriter("output.csv");
var csvWriter = new CsvWriter(writer);

await csvWriter.BeginRowAsync();
await csvWriter.WriteValueAsync("First");
await csvWriter.WriteValueAsync("Second");
await csvWriter.WriteValueAsync("Third");

await csvWriter.BeginRowAsync();
await csvWriter.WriteValuesAsync("Fourth", "Fifth", "Sixth");
```

#### Disabling quotes

```csharp
var csvWriter = new CsvWriter(writer)
{
    Quote = null  // Disable quoting (values won't be escaped)
};

await csvWriter.WriteRowAsync("A\"", "B");  // Output: A",B
```

## Special Cases

### Quoted Values

The library properly handles:
- Multi-line values within quotes
- Escaped quotes (doubled quotes: `""`)
- Values containing separators
- Quotes at the beginning, middle, or end of values

```csharp
// Input CSV:
// "Value with, comma","Normal value","Value with ""quotes"""

// Correctly parsed as:
// - "Value with, comma"
// - "Normal value"
// - "Value with "quotes""
```
