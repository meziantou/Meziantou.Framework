---
title: Getting started
description: Install the package, read the rows of a CSV file by column name, and write a CSV file.
---

## Install the package

Add the `Meziantou.Framework.Csv` package to a project:

```bash
dotnet add package Meziantou.Framework.Csv
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Csv;`.

## Read a file

Create a [CsvReader](cref:Meziantou.Framework.Csv.CsvReader) over a `StreamReader`, and call [ReadRowAsync](cref:Meziantou.Framework.Csv.CsvReader.ReadRowAsync) until it returns `null`. With [HasHeaderRow](cref:Meziantou.Framework.Csv.CsvReader.HasHeaderRow), the
first row gives the names of the columns, and each row gives its values by name:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_ReadFile;format=md-fenced-code-block;dedent=true -->
```csharp
using var reader = new StreamReader("people.csv");
var csvReader = new CsvReader(reader) { HasHeaderRow = true };

var names = new List<string>();
var totalAge = 0;
while (await csvReader.ReadRowAsync() is { } row)
{
    names.Add(row["Name"]!);
    totalAge += int.Parse(row["Age"]!, CultureInfo.InvariantCulture);
}

Assert.Equal(["Alice", "Bob"], names);
Assert.Equal(55, totalAge);
```
<!-- endref -->

- The header row is not returned: the first row [ReadRowAsync](cref:Meziantou.Framework.Csv.CsvReader.ReadRowAsync) returns is the first row of data.
- Values are strings. Parse them with the invariant culture when the file comes from another program, so that the
  result does not depend on the culture of the machine.
- The reader does not own the `StreamReader`: dispose it yourself.

## Write a file

[WriteRowAsync](cref:Meziantou.Framework.Csv.CsvWriter.WriteRowAsync) writes a row. Write the header row first, like any other row:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_WriteFile;format=md-fenced-code-block;dedent=true -->
```csharp
await using (var writer = new StreamWriter("export.csv"))
{
    var csvWriter = new CsvWriter(writer);
    await csvWriter.WriteRowAsync("Name", "Age");
    await csvWriter.WriteRowAsync("Alice", "30");
} // Disposing the StreamWriter flushes the data to the file

var content = await File.ReadAllTextAsync("export.csv");
Assert.Equal($"Name,Age{Environment.NewLine}Alice,30", content);
```
<!-- endref -->

- Rows end with `Environment.NewLine`; the last row has no line terminator. See
  [Line terminators](04-WritingCsv.md#line-terminators).
- The writer does not flush the `StreamWriter`. Dispose it, or call `FlushAsync`, before reading the file.

## Next steps

- [Reading CSV](03-ReadingCsv.md): headers, separators, quoted values, short rows and positions.
- [Writing CSV](04-WritingCsv.md): quoting, separators and writing a row value by value.
