# Meziantou.Framework.Yaml

`Meziantou.Framework.Yaml` is a YAML parser and serializer for .NET. It can read and write YAML documents, serialize object graphs, deserialize typed models, and generate serialization metadata at compile time for NativeAOT and trimming scenarios.

The package includes the source generator automatically. No additional package is required to use generated `YamlSerializerContext` types.

## Install the package

```bash
dotnet add package Meziantou.Framework.Yaml
```

## Serialize and deserialize objects

```csharp
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Serialization;

var options = new YamlSerializerOptions
{
    PropertyNamingPolicy = YamlNamingPolicy.KebabCaseLower,
    WriteIndented = true,
};

var yaml = YamlSerializer.Serialize(new Product
{
    Id = 1,
    DisplayName = "Sample product",
    Tags = ["new", "featured"],
}, options);

var product = YamlSerializer.Deserialize<Product>(yaml, options);

public sealed class Product
{
    public int Id { get; set; }

    [YamlPropertyName("name")]
    public string DisplayName { get; set; } = "";

    public string[] Tags { get; set; } = [];
}
```

`YamlSerializer` supports strings, booleans, numeric types, enums, nullable values, dates and times, GUIDs, URIs, arrays, collections, dictionaries, and object graphs. It also supports YAML anchors, aliases, merge keys, extension data, polymorphism, custom converters, and common serializer options such as field inclusion, required constructor parameters, nullable annotations, read-only member handling, and unmatched property handling.

## Use source generation

Declare a partial context derived from `YamlSerializerContext` and annotate each root type with `YamlSerializableAttribute`.

```csharp
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Serialization;

[YamlSerializable(typeof(Product))]
[YamlSourceGenerationOptions(
    PropertyNamingPolicy = YamlKnownNamingPolicy.KebabCaseLower,
    WriteIndented = true)]
public sealed partial class AppYamlContext : YamlSerializerContext
{
}

var yaml = YamlSerializer.Serialize(product, AppYamlContext.Default.Product);
var copy = YamlSerializer.Deserialize(yaml, AppYamlContext.Default.Product);
```

You can also pass the generated context to the serializer:

```csharp
var yaml = YamlSerializer.Serialize(product, AppYamlContext.Default);
var copy = YamlSerializer.Deserialize<Product>(yaml, AppYamlContext.Default);
```

Source generation avoids reflection-based metadata discovery and is the preferred mode for NativeAOT and trimming-sensitive applications. The generated context can be configured with `YamlSourceGenerationOptionsAttribute` or by constructing the context with a `YamlSerializerOptions` instance.

## Parse and emit YAML documents

Use the DOM APIs when you need to inspect or transform YAML without binding to a CLR type.

```csharp
using Meziantou.Framework.Yaml.Model;

var stream = YamlStream.Load("""
    product:
      id: 1
      name: Sample product
    """);

var document = stream[0];
var root = (YamlMapping)document.Contents!;
var product = (YamlMapping)root["product"];
var name = ((YamlValue)product["name"]).Value;
```

The lower-level parser and emitter APIs are also available for event-based processing.

## Configure serialization

`YamlSerializerOptions` controls how YAML is read and written:

```csharp
var options = new YamlSerializerOptions
{
    PropertyNamingPolicy = YamlNamingPolicy.SnakeCaseLower,
    DictionaryKeyPolicy = YamlNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingNull,
    MappingOrder = YamlMappingOrderPolicy.Sorted,
    RejectUnmatchedProperties = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
};
```

Attributes can be used to configure individual types and members:

- `YamlPropertyNameAttribute`
- `YamlIgnoreAttribute`
- `YamlRequiredAttribute`
- `YamlConstructorAttribute`
- `YamlConverterAttribute`
- `YamlExtensionDataAttribute`
- `YamlPolymorphicAttribute`
- `YamlDerivedTypeAttribute`
- `YamlNumberHandlingAttribute`
- `YamlObjectCreationHandlingAttribute`

Custom converters derive from `YamlConverter<T>` and can be registered through `YamlSerializerOptions.Converters` or `YamlSourceGenerationOptionsAttribute.Converters`.

## Feature switches

Reflection-based serialization can be disabled for applications that only use source-generated metadata. Set the `MeziantouFrameworkYamlIsReflectionEnabledByDefault` MSBuild property to `false` in the project file:

```xml
<PropertyGroup>
  <MeziantouFrameworkYamlIsReflectionEnabledByDefault>false</MeziantouFrameworkYamlIsReflectionEnabledByDefault>
</PropertyGroup>
```

When reflection is disabled, use source-generated `YamlSerializerContext` metadata for typed serialization and deserialization.
