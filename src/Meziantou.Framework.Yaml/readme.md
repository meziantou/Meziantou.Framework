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

Non-public members annotated with `YamlIncludeAttribute` and non-public constructors annotated with `YamlConstructorAttribute` are supported: the generated context reaches them through `UnsafeAccessorAttribute`, which NativeAOT and trimming understand.

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
- `YamlNamingPolicyAttribute`
- `YamlIgnoreAttribute`
- `YamlIncludeAttribute`
- `YamlRequiredAttribute`
- `YamlConstructorAttribute`
- `YamlConverterAttribute`
- `YamlExtensionDataAttribute`
- `YamlPolymorphicAttribute`
- `YamlDerivedTypeAttribute`
- `YamlNumberHandlingAttribute`
- `YamlObjectCreationHandlingAttribute`

`YamlNamingPolicyAttribute` overrides `YamlSerializerOptions.PropertyNamingPolicy`. When applied to a type, it sets the naming policy for all its members; when applied to a member, it only applies to that member. `YamlPropertyNameAttribute` takes precedence over both.

```csharp
[YamlNamingPolicy(YamlKnownNamingPolicy.SnakeCaseLower)]
internal sealed class Product
{
    public int Id { get; set; }                       // id

    [YamlNamingPolicy(YamlKnownNamingPolicy.CamelCase)]
    public string? DisplayName { get; set; }          // displayName

    public DateTime CreationDate { get; set; }        // creation_date
}
```

`YamlIgnoreAttribute` overrides `YamlSerializerOptions.DefaultIgnoreCondition`. When applied to a type, it sets the default ignore condition for all its properties and fields; when applied to a member, it only applies to that member.

```csharp
[YamlIgnore(Condition = YamlIgnoreCondition.WhenWritingNull)]
internal sealed class Product
{
    public string? Name { get; set; }                 // omitted when null

    [YamlIgnore(Condition = YamlIgnoreCondition.Never)]
    public string? Description { get; set; }          // always written

    [YamlIgnore]
    public string? Secret { get; set; }               // never serialized nor deserialized
}
```

Custom converters derive from `YamlConverter<T>` and can be registered through `YamlSerializerOptions.Converters` or `YamlSourceGenerationOptionsAttribute.Converters`.

`YamlConverterAttribute` also accepts an open generic converter type when the annotated type, or the type of the annotated member, is a generic type with the same number of generic parameters. The converter is closed over the type arguments of the target type.

```csharp
[YamlConverter(typeof(BoxConverter<>))]
internal sealed class Box<T>
{
    public T? Value { get; set; }
}

internal sealed class BoxConverter<T> : YamlConverter<Box<T>>  // used for Box<int>, Box<string>, ...
{
}
```

`YamlDerivedTypeAttribute` accepts an open generic derived type on a generic base type. The derived type is closed for each instantiation of the base type by matching the base type it declares, so a single attribute covers every instantiation.

```csharp
[YamlPolymorphic]
[YamlDerivedType(typeof(Dog<>), "dog")]
internal abstract class Animal<T>
{
}

internal sealed class Dog<T> : Animal<T>  // Animal<string> uses Dog<string>, Animal<int> uses Dog<int>
{
}
```

## Feature switches

Reflection-based serialization can be disabled for applications that only use source-generated metadata. Set the `MeziantouFrameworkYamlIsReflectionEnabledByDefault` MSBuild property to `false` in the project file:

```xml
<PropertyGroup>
  <MeziantouFrameworkYamlIsReflectionEnabledByDefault>false</MeziantouFrameworkYamlIsReflectionEnabledByDefault>
</PropertyGroup>
```

When reflection is disabled, use source-generated `YamlSerializerContext` metadata for typed serialization and deserialization.
