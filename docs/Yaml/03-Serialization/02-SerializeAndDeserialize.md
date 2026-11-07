---
title: Serialize and deserialize
description: Convert objects to and from strings, files and streams, configure and reuse options, and read without exceptions.
---

## Strings

[YamlSerializer.Serialize](cref:Meziantou.Framework.Yaml.YamlSerializer.Serialize) returns the YAML text of a value, and [YamlSerializer.Deserialize\<T\>](cref:Meziantou.Framework.Yaml.YamlSerializer.Deserialize) creates a value from
YAML text:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializer_SerializeAndDeserialize;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new YamlSerializerOptions
{
    PropertyNamingPolicy = YamlNamingPolicy.KebabCaseLower,
};

var yaml = YamlSerializer.Serialize(new Product
{
    Id = 1,
    DisplayName = "Sample product",
    Tags = ["new", "featured"],
}, options);

Assert.Equal("""
    id: 1
    name: Sample product
    tags:
      - new
      - featured

    """, yaml);

var product = YamlSerializer.Deserialize<Product>(yaml, options);
Assert.Equal("Sample product", product!.DisplayName);
```
<!-- endref -->

The examples of this section use the `Product` class of [Getting started](../02-GettingStarted.md#serialize-a-class).

## Files and streams

Pass a `Stream` to read or write UTF-8 YAML, for instance a file:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializer_Files;format=md-fenced-code-block;dedent=true -->
```csharp
private static void SaveProduct(string path, Product product)
{
    using var stream = File.Create(path);
    YamlSerializer.Serialize(stream, product); // Written as UTF-8
}

private static Product? LoadProduct(string path)
{
    using var stream = File.OpenRead(path);
    return YamlSerializer.Deserialize<Product>(stream);
}
```
<!-- endref -->

| Read from | Write to |
| --- | --- |
| `string` | `string` (the return value) |
| `TextReader`, read as the parser needs it | `TextWriter` |
| `Stream` holding UTF-8 text | `Stream`, written as UTF-8 |
| | `IBufferWriter<char>` |

The methods are synchronous. Each one has a generic form ([Deserialize\<T\>](cref:Meziantou.Framework.Yaml.YamlSerializer.Deserialize)) and a form that takes a `Type`, for a type
known at run time only.

A stream can hold several documents separated by `---`. [Deserialize](cref:Meziantou.Framework.Yaml.YamlSerializer.Deserialize) binds the first one; to read all of them, load
the stream with [`YamlStream`](../05-DocumentObjectModel.md#read-several-documents) and convert each document with
[ToObject\<T\>](cref:Meziantou.Framework.Yaml.Model.YamlNode.ToObject).

## Options

[YamlSerializerOptions](cref:Meziantou.Framework.Yaml.YamlSerializerOptions) configures both directions. [YamlSerializerOptions.Default](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.Default) holds the default values, and is
used when you pass no options. The options are a `record` with `init` properties: create an instance with the values you
need, or derive one from another with `with`:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializerOptions_With;format=md-fenced-code-block;dedent=true -->
```csharp
var strict = YamlSerializerOptions.Default with
{
    UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow,
};

Assert.Equal(1, YamlSerializer.Deserialize<Product>("Id: 1\nPrice: 10")!.Id); // Price is ignored
Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Product>("Id: 1\nPrice: 10", strict));
```
<!-- endref -->

An instance cannot change after it is created, so you can create it once, for instance in a `static readonly` field,
and use it for every call.

Every method accepts, instead of the options, a [YamlSerializerContext](cref:Meziantou.Framework.Yaml.Serialization.YamlSerializerContext) or a [YamlTypeInfo](cref:Meziantou.Framework.Yaml.YamlTypeInfo) produced by the
[source generator](../04-SourceGeneration.md).

The options are described on the page of the feature they control:

- [Members and names](04-MembersAndNames.md): [PropertyNamingPolicy](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.PropertyNamingPolicy), [DictionaryKeyPolicy](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.DictionaryKeyPolicy), [IncludeFields](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.IncludeFields),
  [DefaultIgnoreCondition](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.DefaultIgnoreCondition), [IgnoreReadOnlyProperties](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.IgnoreReadOnlyProperties), [IgnoreReadOnlyFields](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.IgnoreReadOnlyFields).
- [Output format](05-OutputFormat.md): [WriteIndented](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.WriteIndented), [IndentSize](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.IndentSize), [IndentBlockSequences](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.IndentBlockSequences), [MappingOrder](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.MappingOrder),
  [BlockSequenceMappingStyle](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.BlockSequenceMappingStyle), [BlockSequenceSequenceStyle](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.BlockSequenceSequenceStyle), [ScalarStylePreferences](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.ScalarStylePreferences).
- [Reading behavior](06-ReadingBehavior.md): [UnmappedMemberHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.UnmappedMemberHandling), [PropertyNameCaseInsensitive](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.PropertyNameCaseInsensitive),
  [RespectRequiredConstructorParameters](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.RespectRequiredConstructorParameters), [RespectNullableAnnotations](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.RespectNullableAnnotations), [PreferredObjectCreationHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.PreferredObjectCreationHandling),
  [DuplicateKeyHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.DuplicateKeyHandling).
- [Custom converters](07-CustomConverters.md): [Converters](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.Converters).
- [Polymorphism](08-Polymorphism.md) and [C# unions](09-Unions.md): [PolymorphismOptions](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.PolymorphismOptions), [TypeClassifiers](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.TypeClassifiers).
- [Anchors, aliases and merge keys](10-AnchorsAliasesAndMergeKeys.md): [ReferenceHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.ReferenceHandling).
- [Schemas](11-Schemas.md): [Schema](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.Schema), [UseSchema](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.UseSchema).
- [Handling errors](../08-ErrorHandling.md): [SourceName](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.SourceName).
- [Reading untrusted YAML](../09-UntrustedInput.md): [MaxDepth](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.MaxDepth), [AllowAnchors](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.AllowAnchors), [AllowAliases](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.AllowAliases),
  [MaxAliasExpansionNodeCount](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.MaxAliasExpansionNodeCount), [UnsafeAllowDeserializeFromTagTypeName](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.UnsafeAllowDeserializeFromTagTypeName).

## Read without exceptions

[TryDeserialize](cref:Meziantou.Framework.Yaml.YamlSerializer.TryDeserialize) returns `false` instead of throwing a [YamlException](cref:Meziantou.Framework.Yaml.YamlException) when the YAML is invalid or does not match the
type:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializer_TryDeserialize;format=md-fenced-code-block;dedent=true -->
```csharp
if (YamlSerializer.TryDeserialize<Product>("Id: 1", out var value))
{
    Assert.Equal(1, value!.Id);
}

Assert.False(YamlSerializer.TryDeserialize<Product>("Id: not a number", out _));
```
<!-- endref -->

Use it when invalid input is expected, for instance while probing the content of a file. To tell the user what is
wrong, catch the exception instead: see [Handling errors](../08-ErrorHandling.md).
