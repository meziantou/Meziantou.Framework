---
title: Custom converters
description: Convert a type your own way with TomlConverter, register converters, and write enums as names.
---

A converter reads and writes one type. Write one for a type the serializer does not support, or to change how a
supported type is written.

## Write a converter

Derive from `TomlConverter<T>` and implement `Read` and `Write`:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlConverter_Create;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class UpperCaseStringConverter : TomlConverter<string>
{
    public override string Read(TomlReader reader) => reader.GetString().ToUpperInvariant();

    public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value.ToUpperInvariant());
}
```
<!-- endref -->

For open generic types, or types chosen at runtime, derive from `TomlConverterFactory`, which creates the converter of
each type. The converter types are in the [Meziantou.Framework.Toml.Serialization](cref:Meziantou.Framework.Toml.Serialization) namespace.

## Register a converter

Add the converter to the options:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlConverter_Register;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TomlSerializerOptions { Converters = [new UpperCaseStringConverter()] };

var toml = TomlSerializer.Serialize(new ServerConfig(), options);
Assert.Equal("Host = \"LOCALHOST\"\nPort = 8080\n", toml);
```
<!-- endref -->

Or apply it to a type or a member with `[TomlConverter(typeof(...))]`, which also accepts a converter factory. With the
source generator, list it on the context:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlConverter_Context;format=md-fenced-code-block;dedent=true -->
```csharp
[TomlSourceGenerationOptions(Converters = [typeof(UpperCaseStringConverter)])]
[TomlSerializable(typeof(ServerConfig))]
internal sealed partial class ConverterContext : TomlSerializerContext;
```
<!-- endref -->

- The converters of the options win over the built-in converters.
- A converter for `T` on a member also converts a `T?` member, like in `System.Text.Json`.
- The converters listed on a context are chosen when building, so a converter factory listed there is not used
  (warning `MFTOML012`). Apply `[TomlConverter]` to the type or the member instead.
- With the source generator, the converter type must be accessible from the context: public, or internal to the same
  assembly.

## Read and write values

`TomlReader` exposes `Read`, `Skip`, `GetString`, `GetInt64`, `GetDouble`, `GetDecimal`, `GetBoolean`,
`GetTomlDateTime`, `GetRawText` and `PropertyNameEquals`. `TomlWriter` exposes `WritePropertyName`, the `Write...Value`
methods, and the start and end methods of tables, inline tables, arrays and arrays of tables.

A converter either reads its whole value and then calls `Read` once more to move to the token that follows it, or does
not move the reader at all, as in the example above, and the value is skipped for it. A converter that stops inside its
value, or reads past it, is a configuration error, which `TryDeserialize` throws too.

TOML lets a document define a table in several places: an array of tables can continue after another table, and dotted
keys can extend a table later in the file. `TomlReader` parses the whole document on the first `Read`, so it reports
syntax errors before any value is created, and returns each table and array of tables as one block: a converter reads
each key of a table once.

To read a nested value with the serializer, a converter can use the metadata of its options, for example
`reader.Options.GetTypeInfo<T>().Read(reader)`.

## Enums as strings

Enums are written as numbers. `TomlStringEnumConverter` writes their names instead, and reads names, ignoring case, or
numbers. Apply it to an enum or a member with `[TomlConverter]`:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlStringEnumConverter_Enum;format=md-fenced-code-block;dedent=true -->
```csharp
[TomlConverter(typeof(TomlStringEnumConverter))]
public enum LogLevel
{
    Information,
    [TomlStringEnumMemberName("warn")]
    Warning,
}
```
<!-- endref -->

Or add it to the converters of the options, or of a context, to apply it to every enum:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlStringEnumConverter_Options;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TomlSerializerOptions { Converters = [new TomlStringEnumConverter()] };
```
<!-- endref -->

- `[TomlStringEnumMemberName]` sets the name of a value. That name is the one written, and the one read first: an exact
  match wins over a match in another case. The C# name is read too.
- A flags value is written as a comma-separated list of names. Only a flags enum reads such a list.
