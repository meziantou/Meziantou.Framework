---
title: Getting started
description: Install the package, read the records of a CSV file by column name, write a CSV file, and map the records to objects.
---

## Install the package

Add the `Meziantou.Framework.Csv` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Csv" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Csv;`, and
`using Meziantou.Framework.Csv.Serialization;` for the serializer.

## Read a file

[CsvReader.CreateFromFile](cref:Meziantou.Framework.Csv.CsvReader.CreateFromFile) opens a file. Call [ReadAsync](cref:Meziantou.Framework.Csv.CsvReader.ReadAsync) or [Read](cref:Meziantou.Framework.Csv.CsvReader.Read) until it returns `false`, and get the values of the
[Current](cref:Meziantou.Framework.Csv.CsvReader.Current) record by column name:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvReaderTests.cs;region=CsvReader_ReadFile;format=md-fenced-code-block;dedent=true -->
```csharp
await using var reader = CsvReader.CreateFromFile("people.csv");

var names = new List<string>();
var totalAge = 0;
while (await reader.ReadAsync())
{
    var record = reader.Current;
    names.Add(record["Name"].ToString());
    totalAge += record["Age"].Parse<int>();
}

Assert.Equal(["Alice", "Bob"], names);
Assert.Equal(55, totalAge);
```
<!-- endref -->

- The first row is the header row by default: it gives the names of the columns and is not returned as a record.
- [Parse](cref:Meziantou.Framework.Csv.CsvField.Parse) parses a value with the invariant culture, so that the result does not depend on the culture of the machine.
- Dispose the reader to close the file.

## Write a file

[CsvWriter.CreateToFile](cref:Meziantou.Framework.Csv.CsvWriter.CreateToFile) creates a file. Write the header row like any other row:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvWriterTests.cs;region=CsvWriter_WriteFile;format=md-fenced-code-block;dedent=true -->
```csharp
await using (var writer = CsvWriter.CreateToFile("export.csv"))
{
    writer.WriteRow("Name", "Age");
    await writer.WriteRowAsync(["Alice", "30"]);
} // Disposing the writer flushes the data and closes the file

var content = await File.ReadAllTextAsync("export.csv");
Assert.Equal("Name,Age\r\nAlice,30\r\n", content);
```
<!-- endref -->

Every row ends with `\r\n` by default, including the last one. Disposing the writer flushes the data.

## Map the records to objects

[CsvSerializer](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer) writes the public properties of objects as columns, and reads them back:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_PersonModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Person
{
    public string? Name { get; set; }

    public int Age { get; set; }

    public DateOnly? BirthDate { get; set; }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_RoundTrip;format=md-fenced-code-block;dedent=true -->
```csharp
var people = new List<Person>
{
    new() { Name = "Alice", Age = 30, BirthDate = new DateOnly(1995, 4, 12) },
    new() { Name = "Bob, Jr.", Age = 25 },
};

var csv = CsvSerializer.Serialize(people);
Assert.Equal("Name,Age,BirthDate\r\nAlice,30,1995-04-12\r\n\"Bob, Jr.\",25,\r\n", csv);

var result = CsvSerializer.Deserialize<Person>(csv);
Assert.Equal("Bob, Jr.", result[1].Name);
Assert.Null(result[1].BirthDate); // An empty value is null for a nullable type
```
<!-- endref -->

## Next steps

- [Reading CSV](03-ReadingCsv.md): headers, separators, quoted values, comments, positions and UTF-8.
- [Writing CSV](04-WritingCsv.md): quoting, separators, formatting and asynchronous writing.
- [Serialization](05-Serialization.md): records, attributes, naming policies and streams.
