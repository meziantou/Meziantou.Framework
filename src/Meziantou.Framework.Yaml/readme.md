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

`YamlExtensionDataAttribute` collects the mapping keys that don't match any member. The annotated member must be a `YamlMapping`, or a dictionary whose keys are `string` and whose values are `object` or `YamlNode`:

```csharp
internal sealed class Product
{
    public int Id { get; set; }

    [YamlExtensionData]
    public IReadOnlyDictionary<string, object?>? Extra { get; set; }
}
```

The supported dictionary types are `Dictionary<string, TValue>`, `IDictionary<string, TValue>`, and `IReadOnlyDictionary<string, TValue>`. A read-only dictionary cannot be mutated through its declared type, so the deserializer creates a `Dictionary<string, TValue>`, copies the entries of the current value into it, and assigns it to the member. The member must be settable, unless its value is already a `Dictionary<string, TValue>` which is then updated in place.

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

### Closed hierarchies

A `closed` class hierarchy already lists its derived types in metadata, so they do not need to be repeated with `YamlDerivedTypeAttribute`. Set `InferClosedTypePolymorphism` to register every direct derived type of a closed base type automatically, using the derived type name, without the generic arity suffix, as its discriminator.

```csharp
public closed class Shape
{
    public string Name { get; set; } = "";
}

public sealed class Circle : Shape { public double Radius { get; set; } }
public sealed class Square : Shape { public double Side { get; set; } }

var options = new YamlSerializerOptions
{
    PolymorphismOptions = new YamlPolymorphismOptions { InferClosedTypePolymorphism = true },
};

Shape shape = new Circle { Name = "circle", Radius = 3 };
var yaml = YamlSerializer.Serialize(shape, options); // $type: Circle
```

Inference is opt-in. It can also be enabled for a single type with `YamlPolymorphicAttribute`, which takes precedence over the serializer options, so `InferClosedTypePolymorphism = false` excludes a type from a global opt-in:

```csharp
[YamlPolymorphic(InferClosedTypePolymorphism = true)]
public closed class Shape
{
}
```

Explicit registrations replace inference: a type declaring `YamlDerivedTypeAttribute` or a runtime mapping registers only those derived types. Enabling `YamlPolymorphicAttribute.InferClosedTypePolymorphism` on a type that is not declared `closed` throws an `InvalidOperationException` and reports the `MFY023` diagnostic. Only the direct derived types of the closed base type are registered, as with explicit registrations: when a derived type is itself `closed`, its own derived types are registered under it and not under the root of the hierarchy.

A derived type must be at least as visible as the base type it is registered under, and two derived types cannot share a name. Reflection-based serialization throws an `InvalidOperationException` in those cases, and the source generator skips the derived type and reports the `MFY025` diagnostic.

Source generation exposes the same setting on `YamlSourceGenerationOptionsAttribute`:

```csharp
[YamlSourceGenerationOptions(InferClosedTypePolymorphism = true)]
[YamlSerializable(typeof(Shape))]
internal sealed partial class ShapeYamlContext : YamlSerializerContext
{
}
```

## C# unions

A C# union is serialized as its selected case, without a wrapper:

```csharp
internal union Setting(bool, int, string);

YamlSerializer.Serialize(new Setting(42)); // 42
```

Cases are selected by YAML shape when deserializing, so a union whose cases use distinct shapes needs no configuration. A payload matching several cases &mdash; two cases that both serialize as a mapping, for instance &mdash; fails unless a type classifier is registered. `YamlUnionTypeStructuralClassifier` tells mapping cases apart by their keys:

```csharp
internal union Shape(Circle, Rectangle);
internal sealed class Circle { public int Radius { get; set; } }
internal sealed class Rectangle { public int Width { get; set; } public int Height { get; set; } }

var options = new YamlSerializerOptions
{
    TypeClassifiers = [new YamlUnionTypeStructuralClassifier()],
};

YamlSerializer.Deserialize<Shape>("Radius: 3", options); // Circle
```

The classifier starts with every mapping case as a candidate and eliminates the cases that do not declare a key present in the payload. Keys no case declares eliminate only the cases that reject unmapped members, and a case missing a required key is eliminated once the mapping has been read. Deserialization succeeds when exactly one candidate remains. A union declaring cases that can never be told apart is rejected when the classifier is created.

Implement `YamlTypeClassifierFactory` to select cases with your own rules.

## Feature switches

Reflection-based serialization can be disabled for applications that only use source-generated metadata. Set the `MeziantouFrameworkYamlIsReflectionEnabledByDefault` MSBuild property to `false` in the project file:

```xml
<PropertyGroup>
  <MeziantouFrameworkYamlIsReflectionEnabledByDefault>false</MeziantouFrameworkYamlIsReflectionEnabledByDefault>
</PropertyGroup>
```

When reflection is disabled, use source-generated `YamlSerializerContext` metadata for typed serialization and deserialization.
