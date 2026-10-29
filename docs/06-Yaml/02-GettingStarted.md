---
title: Getting started
description: Install the package, serialize a class to YAML, read it back, and read a document without a class.
---

## Install the package

Add the `Meziantou.Framework.Yaml` package to your project:

```bash
dotnet add package Meziantou.Framework.Yaml
```

The package also contains the source generator: no other package is needed for
[source generation](04-SourceGeneration.md).

The examples of this documentation omit the `using` directives. The serializer needs `using Meziantou.Framework.Yaml;`
and the attributes `using Meziantou.Framework.Yaml.Serialization;`; see [Namespaces](01-Introduction.md#namespaces) for
the others.

## Serialize a class

Given this class:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerSourceGenerationTests.cs;region=YamlSerializer_Product;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class Product
{
    public int Id { get; set; }

    [YamlPropertyName("name")]
    public string DisplayName { get; set; } = "";

    public string[] Tags { get; set; } = [];
}
```
<!-- endref -->

`YamlSerializer.Serialize` writes an instance as YAML, and `YamlSerializer.Deserialize` reads it back:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializer_SerializeAndDeserialize;format=md-fenced-code-block;dedent=true -->
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

- The public properties are written in their declaration order. `PropertyNamingPolicy` converts their names, here to
  kebab case; `[YamlPropertyName]` sets the name of one property and wins over the policy.
- The array is written as a block sequence, indented under its key.
- Reading accepts the same options. A key that matches no property is skipped by default.

## Read a document without a class

When you only need a few values, or the document has no fixed shape, load it as a tree of nodes instead:

<!-- ref:../../tests/Meziantou.Framework.Yaml.Tests/YamlNodeTest.cs;region=YamlStream_Load;format=md-fenced-code-block;dedent=true -->
```csharp
var stream = YamlStream.Load(new StringReader("""
    product:
      id: 1
      name: Sample product
    """));

var document = stream[0];
var root = (YamlMapping)document.Contents!;
var product = (YamlMapping)root["product"]!;
var name = ((YamlValue)product["name"]!).Value;
Assert.Equal("Sample product", name);
```
<!-- endref -->

A `YamlStream` holds every document of the text; each document contains mappings (`YamlMapping`), sequences
(`YamlSequence`) and scalars (`YamlValue`). See [Document object model](05-DocumentObjectModel.md).

## Next steps

- [Serialize and deserialize](03-Serialization/02-SerializeAndDeserialize.md): files, streams and reusable options.
- [Members and names](03-Serialization/04-MembersAndNames.md) and [Output format](03-Serialization/05-OutputFormat.md):
  control the YAML you write.
- [Source generation](04-SourceGeneration.md): serialize without reflection, for NativeAOT and trimming.
- [Handling errors](08-ErrorHandling.md) and [Reading untrusted YAML](09-UntrustedInput.md).
