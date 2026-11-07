---
title: Options
description: Choose the converters to generate, how Guid values are created, how strings are compared and how ToString formats an ID.
---

The options are arguments of `[StronglyTypedId]`. Set them on one type, or for every type of the assembly with
[assembly-wide defaults](08-AssemblyDefaults.md).

## All the options

| Option | Default | Effect |
| --- | --- | --- |
| `generateSystemTextJsonConverter` | `true` | Generates the [System.Text.Json converter](09-Integrations/02-SystemTextJson.md). |
| `generateNewtonsoftJsonConverter` | `true` | Generates the [Newtonsoft.Json converter](09-Integrations/03-NewtonsoftJson.md). |
| `generateSystemComponentModelTypeConverter` | `true` | Generates the [`TypeConverter`](09-Integrations/05-TypeConverterAndConfiguration.md). |
| `generateMongoDBBsonSerialization` | `true` | Generates the [MongoDB serializer](09-Integrations/04-MongoDB.md). |
| `addCodeGeneratedAttribute` | `true` | Marks the generated members with `[GeneratedCode]`, which tools use to recognize generated code. |
| `StringComparison` | `Ordinal` | How `string` values are compared. |
| `GenerateToStringAsRecord` | `true` | Formats `ToString()` as `ProjectId { Value = 42 }`; `false` returns the value only. |
| `GuidGenerationStrategy` | `Version4` | How `New()` creates `Guid` values, or `None` to not generate it. |

The first five options are optional constructor parameters, written with a colon (`generateNewtonsoftJsonConverter: false`).
The last three are properties, written with an equals sign (`StringComparison = StringComparison.OrdinalIgnoreCase`):

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_Configuration;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId(idType: typeof(long),
                 generateSystemTextJsonConverter: true,
                 generateNewtonsoftJsonConverter: true,
                 generateSystemComponentModelTypeConverter: true,
                 generateMongoDBBsonSerialization: true,
                 addCodeGeneratedAttribute: true
                 )]
public partial struct ShipmentId { }
```
<!-- endref -->

## Choose the converters

A converter is generated when its option is `true` and the project can use it: the Newtonsoft.Json converter needs a
reference to Newtonsoft.Json, and the MongoDB serializer one to MongoDB.Bson. A project that references neither gets
neither, whatever the options say. Set an option to `false` to skip a converter that the project could use but that this
ID should not have:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_DisableConverters;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<int>(generateNewtonsoftJsonConverter: false, generateMongoDBBsonSerialization: false)]
public partial struct TagId { }
```
<!-- endref -->

`TagId` keeps its System.Text.Json converter and its `TypeConverter`.

## Create Guid values

For a `Guid` ID, the generator adds `New()`, which creates an ID from a new `Guid`. `GuidGenerationStrategy` chooses
how:

| Value | `New()` |
| --- | --- |
| `Version4` (default) | Calls `Guid.NewGuid()`: a random value. |
| `Version7` | Calls `Guid.CreateVersion7()`: a time-ordered value, which starts with the time it was created. Requires .NET 9 or later. |
| `None` | Is not generated. Use it for IDs that come from another system, which the application never creates. |

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_GuidVersion7;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<Guid>(GuidGenerationStrategy = GuidGenerationStrategy.Version7)]
public partial struct InvoiceId { }
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_GuidVersion7Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var id = InvoiceId.New(); // Uses Guid.CreateVersion7()
Assert.Equal(7, id.Value.Version);
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_GuidNone;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<Guid>(GuidGenerationStrategy = GuidGenerationStrategy.None)]
public partial struct ExternalId { } // No New() method is generated
```
<!-- endref -->

`Version7` on a framework without `Guid.CreateVersion7()` is an error
([MFSTID0002](11-Diagnostics.md#mfstid0002-guid-generation-strategy-is-not-supported-by-the-target-framework)), and the
option on an ID that does not wrap a `Guid` is a warning
([MFSTID0003](11-Diagnostics.md#mfstid0003-guid-generation-strategy-is-only-applicable-to-guid)).

## Compare string values

`string` values are compared with an ordinal, case-sensitive comparison. Choose another comparison with
`StringComparison`:

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_StringComparison;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<string>(StringComparison = StringComparison.OrdinalIgnoreCase)]
public partial struct CountryCode { }
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_StringComparisonUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var code = CountryCode.FromString("fr");
Assert.Equal(CountryCode.FromString("FR"), code);
Assert.Equal(CountryCode.FromString("FR").GetHashCode(), code.GetHashCode());

// The value is kept as it was created
Assert.Equal("fr", code.Value);
```
<!-- endref -->

`Equals`, `GetHashCode`, `==` and `!=` use the comparison, so IDs that differ only by case are the same key in a
dictionary. The value is not normalized: `Value` and the serialized value keep the case they were created with. The
option has no effect on other underlying types, nor on [sorting](03-DeclaringIds.md#implement-icomparable-to-sort-ids).

## Format ToString as the value

By default, `ToString()` returns the name of the type and the value, like a record: `ProjectId { Value = 42 }`. It
makes logs and debugger views clear about what the number is. Set `GenerateToStringAsRecord` to `false` to return
the value only, the same text as [ValueAsString](cref:Meziantou.Framework.IStronglyTypedId.ValueAsString):

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_ToStringAsValue;format=md-fenced-code-block;dedent=true -->
```csharp
[StronglyTypedId<int>(GenerateToStringAsRecord = false)]
public partial struct InvoiceNumber { }
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_ToStringAsValueUsage;format=md-fenced-code-block;dedent=true -->
```csharp
var number = InvoiceNumber.FromInt32(1042);
Assert.Equal("1042", number.ToString());
Assert.Equal("Invoice 1042", $"Invoice {number}");
```
<!-- endref -->

String interpolation without a format uses `ToString()` too. To build URLs or messages from the value whatever this
option, use [ValueAsString](cref:Meziantou.Framework.IStronglyTypedId.ValueAsString) or a format such as `$"{id:D}"`.
