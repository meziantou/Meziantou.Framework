---
title: Serialization
description: Read CSV data as a list of objects and write objects as CSV data, with records, attributes, naming policies, streams and error handling.
---

[CsvSerializer](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer) maps each record to an object and each column to a property or a field. Columns are found by name in the
header row:

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

- The columns are the public properties with a public getter or setter, in declaration order, the properties of base
  types first. Public fields are included when [IncludeFields](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.IncludeFields) is `true`.
- An empty value is `null` for a nullable type and an empty string for a `string`. A `null` value is written as an
  empty value.
- Empty lines are skipped, and the members without a column keep their default value.
- The overloads that take a [CsvSerializerOptions](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions) use reflection. To use the serializer with trimming or Native
  AOT, see [Source generation](07-SourceGeneration.md).

## Inputs and outputs

[Deserialize](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.Deserialize) reads a string, UTF-8 bytes, a `TextReader` or a `Stream` and returns a `List<T>`. To process a large file
without loading every record, [DeserializeEnumerable](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.DeserializeEnumerable) and [DeserializeAsyncEnumerable](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.DeserializeAsyncEnumerable) read the records as they are
enumerated:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_Stream;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream();
await CsvSerializer.SerializeAsync(stream, [new Person { Name = "Alice", Age = 30 }, new Person { Name = "Bob", Age = 25 }]);

// Read the records one by one, without loading the whole file
stream.Position = 0;
var names = new List<string?>();
await foreach (var person in CsvSerializer.DeserializeAsyncEnumerable<Person>(stream))
{
    names.Add(person.Name);
}

Assert.Equal(["Alice", "Bob"], names);
```
<!-- endref -->

[Serialize](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.Serialize) writes to a string, a `Stream`, a `TextWriter` or an `IBufferWriter<byte>`, and [SerializeAsync](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.SerializeAsync)
accepts an `IAsyncEnumerable<T>`. The overloads that take a `Stream` read and write UTF-8 without converting the data to
UTF-16.

## Records and constructors

The serializer creates the instances with the public parameterless constructor, or with the public constructor when
the type has only one, such as a positional record. The parameters are matched to the columns by name, ignoring case:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_ProductModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed record Product(int Id, string Name, decimal Price);
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_Records;format=md-fenced-code-block;dedent=true -->
```csharp
var products = CsvSerializer.Deserialize<Product>("""
    Id,Name,Price
    1,Keyboard,49.90
    2,Mouse,19.99
    """);

// Records are created with their constructor; the parameters are matched to the columns by name
Assert.Equal(new Product(2, "Mouse", 19.99m), products[1]);
```
<!-- endref -->

- Mark a constructor with [CsvConstructorAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvConstructorAttribute) to select it when the type has several.
- A parameter without a column gets its default value.
- A parameter without a matching member is a column that is read but not written.

## Attributes

| Attribute | Effect |
| --- | --- |
| [CsvPropertyNameAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvPropertyNameAttribute) | Sets the name of the column. |
| [CsvPropertyOrderAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvPropertyOrderAttribute) | Sets the order of the column; the default order is 0. |
| [CsvColumnIndexAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvColumnIndexAttribute) | Sets the position of the column for data without a header row. |
| [CsvIgnoreAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvIgnoreAttribute) | Excludes the member. |
| [CsvIncludeAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvIncludeAttribute) | Includes a non-public member, a field, or a non-public accessor. |
| [CsvRequiredAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvRequiredAttribute) | Requires the column, like the C# `required` keyword. |
| [CsvFormatAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvFormatAttribute) | Sets the format string, used to write the values and to read the dates and times. |
| [CsvConverterAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvConverterAttribute) | Sets the converter of the member. See [Converters](06-Converters.md). |

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_OrderModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Order
{
    [CsvPropertyOrder(-1)] // Written first
    public int Id { get; set; }

    [CsvPropertyName("customer_name")]
    public string? Customer { get; set; }

    [CsvFormat("dd/MM/yyyy")]
    public DateTime Date { get; set; }

    [CsvIgnore]
    public string? Secret { get; set; }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_Attributes;format=md-fenced-code-block;dedent=true -->
```csharp
var orders = new[] { new Order { Id = 1, Customer = "Alice", Date = new DateTime(2026, 11, 21), Secret = "hidden" } };

var csv = CsvSerializer.Serialize(orders);
Assert.Equal("Id,customer_name,Date\r\n1,Alice,21/11/2026\r\n", csv);

var order = CsvSerializer.Deserialize<Order>(csv)[0];
Assert.Equal(new DateTime(2026, 11, 21), order.Date); // Read with the format of CsvFormatAttribute
Assert.Null(order.Secret);
```
<!-- endref -->

## Column names

[PropertyNamingPolicy](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.PropertyNamingPolicy) converts the names of the members, for instance to snake case, and [PropertyNameCaseInsensitive](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.PropertyNameCaseInsensitive)
matches the columns ignoring case:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_NamingPolicy;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvSerializerOptions
{
    PropertyNamingPolicy = CsvNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
};

var csv = CsvSerializer.Serialize([new Product(1, "Keyboard", 49.90m)], options);
Assert.Equal("id,name,price\r\n1,Keyboard,49.90\r\n", csv);

var products = CsvSerializer.Deserialize<Product>("ID,NAME,PRICE\n2,Mouse,19.99", options);
Assert.Equal("Mouse", products[0].Name);
```
<!-- endref -->

[CsvNamingPolicy](cref:Meziantou.Framework.Csv.Serialization.CsvNamingPolicy) has camel case, Pascal case, snake case and kebab case policies. A name set with [CsvPropertyNameAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvPropertyNameAttribute) is
not converted.

## Separators, cultures and other dialects

[CsvSerializerOptions](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions) has the options of the readers and the writers: [Separator](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.Separator), [Quote](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.Quote), [NewLine](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.NewLine), [Trim](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.Trim),
[CommentCharacter](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.CommentCharacter), [QuoteMode](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.QuoteMode), [EscapeFormulas](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.EscapeFormulas) and [Strict](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.Strict). [FormatProvider](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.FormatProvider) parses and formats the values, the invariant
culture by default:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_Dialect;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvSerializerOptions
{
    Separator = ';',
    NewLine = "\n",
    FormatProvider = new NumberFormatInfo { NumberDecimalSeparator = "," },
};

var csv = CsvSerializer.Serialize([new Product(1, "Keyboard", 49.90m)], options);
Assert.Equal("Id;Name;Price\n1;Keyboard;49,90\n", csv);
Assert.Equal(49.90m, CsvSerializer.Deserialize<Product>(csv, options)[0].Price);
```
<!-- endref -->

The options are immutable and cache the metadata of the types: create them once and reuse them. Copy them with
`new CsvSerializerOptions(options) { ... }` to change a value.

## Data without a header row

Set [HasHeaderRow](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.HasHeaderRow) to `false` to read and write the columns by position. A member is the column at its position in
the list of the members, or at the index set with [CsvColumnIndexAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvColumnIndexAttribute); the columns no member uses are written empty:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_MeasureModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Measure
{
    [CsvColumnIndex(0)]
    public DateOnly Date { get; set; }

    [CsvColumnIndex(2)]
    public double Value { get; set; }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_Headerless;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new CsvSerializerOptions { HasHeaderRow = false };

// Without a header row, the columns are read and written by position: CsvColumnIndexAttribute sets the position
var measures = CsvSerializer.Deserialize<Measure>("2026-11-21,ignored,12.5\n2026-11-22,ignored,13", options);
Assert.Equal(13, measures[1].Value);

var csv = CsvSerializer.Serialize(measures, options);
Assert.Equal("2026-11-21,,12.5\r\n2026-11-22,,13\r\n", csv);
```
<!-- endref -->

## Missing and unknown columns

By default, a member without a column keeps its default value and a column without a member is ignored.
[MissingColumnHandling](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.MissingColumnHandling) and [UnmappedColumnHandling](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.UnmappedColumnHandling) throw a [CsvException](cref:Meziantou.Framework.Csv.CsvException) instead:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_MissingColumns;format=md-fenced-code-block;dedent=true -->
```csharp
// A member without a column keeps its default value, and a column without a member is ignored
var people = CsvSerializer.Deserialize<Person>("Name,Country\nAlice,France");
Assert.Equal(0, people[0].Age);

var options = new CsvSerializerOptions
{
    MissingColumnHandling = CsvMissingColumnHandling.Throw,
    UnmappedColumnHandling = CsvUnmappedColumnHandling.Throw,
};
Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Person>("Name,Country\nAlice,France", options));
```
<!-- endref -->

A required member, declared with the `required` keyword or [CsvRequiredAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvRequiredAttribute), throws when its column is missing from the
header row, and when a record is too short to have a value for it.

## Errors

A value that cannot be converted throws a [CsvException](cref:Meziantou.Framework.Csv.CsvException) with the position of the value, so that the error can be reported to
whoever produced the file:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerTests.cs;region=CsvSerializer_Errors;format=md-fenced-code-block;dedent=true -->
```csharp
var exception = Assert.Throws<CsvException>(() => CsvSerializer.Deserialize<Product>("Id,Name,Price\n1,Keyboard,49.90\nx,Mouse,19.99"));

Assert.Equal(3, exception.LineNumber);
Assert.Equal(1, exception.RowIndex);
Assert.Equal("Id", exception.ColumnName);
Assert.StartsWith("The value 'x' of the column 'Id' cannot be converted", exception.Message);
```
<!-- endref -->

## Callbacks

A type that implements [ICsvOnDeserializing](cref:Meziantou.Framework.Csv.Serialization.ICsvOnDeserializing), [ICsvOnDeserialized](cref:Meziantou.Framework.Csv.Serialization.ICsvOnDeserialized), [ICsvOnSerializing](cref:Meziantou.Framework.Csv.Serialization.ICsvOnSerializing) or [ICsvOnSerialized](cref:Meziantou.Framework.Csv.Serialization.ICsvOnSerialized) is notified before and
after its members are read or written, for instance to validate a record.
