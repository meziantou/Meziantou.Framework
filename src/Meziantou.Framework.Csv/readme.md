# Meziantou.Framework.Csv

A high-performance .NET library to read and write CSV data, and to map CSV records to .NET types.

## Features

- **SIMD parsing** - The separators, quotes and line terminators are found 16, 32 or 64 characters at a time (AVX-512, AVX2, SSE, NEON), and the values are slices of the buffer: reading a record does not allocate
- **UTF-16 and UTF-8** - `CsvReader` and `CsvWriter` work on text, `Utf8CsvReader` and `Utf8CsvWriter` on UTF-8 bytes without decoding them
- **Any source** - Strings, memory, `TextReader`, `Stream`, `IBufferWriter<T>` and files, synchronously or asynchronously
- **Serializer** - Map records to classes, structs and records with `CsvSerializer`, attributes, naming policies and converters, in the style of System.Text.Json
- **Native AOT** - A source generator writes the serialization code at build time
- **Real-world files** - Custom separators and quotes, separator detection, comments, empty lines, trimming, strict mode, CSV injection protection

## Usage

The types are in the `Meziantou.Framework.Csv` and `Meziantou.Framework.Csv.Serialization` namespaces.

### Reading CSV data

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

### Reading UTF-8 data

Prefer `Utf8CsvReader` for UTF-8 files, streams and bytes: it has the same options and members as `CsvReader`, and does not decode the values you do not read.

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

### Writing CSV data

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

Prefer `Utf8CsvWriter` to write UTF-8 files, streams and `IBufferWriter<byte>`: it formats values directly to UTF-8 bytes.

### Serializing objects

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

`CsvSerializer.SerializeToUtf8Bytes` returns the UTF-8 data as a `byte[]`, without creating a string first.

### Source generation for Native AOT

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerSourceGenerationTests.cs;region=CsvSerializerContext_Declare;format=md-fenced-code-block;dedent=true -->
```csharp
[CsvSerializable(typeof(Product))]
[CsvSerializable(typeof(Person))]
[CsvSourceGenerationOptions(Separator = ';')]
public partial class AppCsvContext : CsvSerializerContext
{
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerSourceGenerationTests.cs;region=CsvSerializerContext_Use;format=md-fenced-code-block;dedent=true -->
```csharp
Product[] products = [new(1, "Keyboard", 49.90m), new(2, "Mouse", 19.99m)];

// The metadata comes from the generated code: no reflection, compatible with trimming and Native AOT
var csv = CsvSerializer.Serialize(products, AppCsvContext.Default.Product);
Assert.Equal("Id;Name;Price\r\n1;Keyboard;49.90\r\n2;Mouse;19.99\r\n", csv);

var result = CsvSerializer.Deserialize(csv, AppCsvContext.Default.Product);
Assert.Equal(products, result);
```
<!-- endref -->
