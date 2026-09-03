# Meziantou.Framework.Yaml

`Meziantou.Framework.Yaml` is a YAML parser, emitter, and serializer for .NET. It reads and writes YAML documents,
serializes object graphs, deserializes typed models, and generates serialization metadata at compile time for
NativeAOT and trimming scenarios.

The source generator ships inside the package. No additional package is required to use generated
`YamlSerializerContext` types.

## Install the package

```bash
dotnet add package Meziantou.Framework.Yaml
```

## Table of contents

- [Overview](#overview)
- [Serialize and deserialize objects](#serialize-and-deserialize-objects)
- [Supported types](#supported-types)
- [Configure serialization](#configure-serialization)
- [Attributes](#attributes)
- [Lifecycle callbacks](#lifecycle-callbacks)
- [Custom converters](#custom-converters)
- [Polymorphism](#polymorphism)
- [C# unions](#c-unions)
- [Type classifiers](#type-classifiers)
- [Anchors, aliases, and merge keys](#anchors-aliases-and-merge-keys)
- [Schemas](#schemas)
- [Source generation](#source-generation)
- [NativeAOT and trimming](#nativeaot-and-trimming)
- [Document Object Model](#document-object-model)
- [Syntax tree](#syntax-tree)
- [Parser and emitter](#parser-and-emitter)
- [Error handling](#error-handling)
- [Hardening untrusted input](#hardening-untrusted-input)

## Overview

The package targets **YAML 1.2**. The parser accepts documents declaring `%YAML 1.1` or `%YAML 1.2`; any other version
directive is rejected with a `SemanticErrorException`. Multi-document streams, `%TAG` directives, block and flow
collections, all five scalar styles (plain, single-quoted, double-quoted, literal, folded), anchors, aliases, merge
keys, tags, and comments are all parsed.

Four API layers are available, from the lowest level to the highest:

| Layer | Entry points | Use it for |
| --- | --- | --- |
| Tokens and events | `Scanner<TBuffer>`, `Parser<TBuffer>`, `EventReader`, `Emitter` | Streaming over parsing events without materializing a tree |
| Syntax tree | `YamlSyntaxTree` | Lossless representation, including comments and whitespace, with source spans |
| Document Object Model | `YamlStream`, `YamlDocument`, `YamlMapping`, `YamlSequence`, `YamlValue` | Inspecting or transforming a document without binding to a CLR type |
| Serializer | `YamlSerializer`, `YamlReader`, `YamlWriter` | Binding YAML to CLR types |

## Serialize and deserialize objects

```csharp
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Serialization;

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

var product = YamlSerializer.Deserialize<Product>(yaml, options);

public sealed class Product
{
    public int Id { get; set; }

    [YamlPropertyName("name")]
    public string DisplayName { get; set; } = "";

    public string[] Tags { get; set; } = [];
}
```

`YamlSerializer` reads from a `string`, a `TextReader` (streaming), or a UTF-8 `Stream`, and writes to a `string`, a
`TextWriter`, a UTF-8 `Stream`, or an `IBufferWriter<char>`. Every overload has a generic and a `Type`-based form, and
accepts either a `YamlSerializerOptions`, a `YamlSerializerContext`, or a `YamlTypeInfo`.

`TryDeserialize` returns `false` instead of throwing when the payload cannot be bound:

```csharp
if (YamlSerializer.TryDeserialize<Product>(yaml, out var value, options))
{
    Use(value);
}
```

When a stream contains several documents, `YamlSerializer.Deserialize` binds the first one. Use
[`YamlStream`](#document-object-model) to read every document.

## Supported types

| Category | Types |
| --- | --- |
| Text | `string`, `char` |
| Boolean | `bool` |
| Integers | `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `nint`, `nuint`, `Int128`, `UInt128` |
| Floating point | `float`, `double`, `decimal`, `Half` |
| Date and time | `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` |
| Other scalars | `Guid`, `Uri`, `CultureInfo`, enums |
| net11.0 only | `BFloat16`, `Decimal32`, `Decimal64`, `Decimal128` |
| Collections | arrays, `List<>`, `HashSet<>`, `ImmutableArray<>`, `ImmutableList<>`, `ImmutableHashSet<>`, `IEnumerable<>`, `ICollection<>`, `IList<>`, `IReadOnlyCollection<>`, `IReadOnlyList<>`, `ISet<>`, `IReadOnlySet<>` |
| Dictionaries | `Dictionary<,>`, `OrderedDictionary<,>`, `IDictionary<,>`, `IReadOnlyDictionary<,>`, with `string` or non-`string` keys |
| Untyped | `object`, `object[]`, `List<object>`, `Dictionary<string, object>` |
| Documents | any `YamlNode` (`YamlMapping`, `YamlSequence`, `YamlValue`, …) |
| Unions | [C# unions](#c-unions) |

Every value type in the table also works as its `Nullable<T>` counterpart. Any type implementing a single
`ICollection<T>` and exposing a parameterless constructor is treated as a collection. Anything else is serialized as a
mapping of its properties, using the object contract described below.

Enums are written using their member name and read case-insensitively, with a numeric fallback. Use
`YamlEnumMemberNameAttribute` to control the name of an individual member.

`Uri` is written using its original string and read with `UriKind.RelativeOrAbsolute`, so both absolute and relative
URIs round-trip. `CultureInfo` is written using its culture name (`CultureInfo.InvariantCulture` writes an empty
name), and read back through `CultureInfo.GetCultureInfo`.

Object contracts support parameterless constructors, parameterized constructors, records, `init` accessors, the
`required` keyword, and non-public members annotated with `YamlIncludeAttribute`. When a type declares several public
constructors, annotate the one to use with `YamlConstructorAttribute`.

## Configure serialization

`YamlSerializerOptions` is a `record`, so an existing instance can be adapted with `with`. `YamlSerializerOptions.Default`
exposes the default instance.

### Naming

| Option | Default | Description |
| --- | --- | --- |
| `PropertyNamingPolicy` | `null` | Converts CLR member names. Built-in policies: `YamlNamingPolicy.CamelCase`, `SnakeCaseLower`, `SnakeCaseUpper`, `KebabCaseLower`, `KebabCaseUpper`, `PascalCase`. Derive from `YamlNamingPolicy` for a custom policy. |
| `DictionaryKeyPolicy` | `null` | Converts dictionary keys when writing. |
| `PropertyNameCaseInsensitive` | `false` | Matches mapping keys to members ignoring case. |

### Member selection

| Option | Default | Description |
| --- | --- | --- |
| `IncludeFields` | `false` | Includes public fields. |
| `IgnoreReadOnlyFields` | `false` | Skips read-only fields when writing. |
| `IgnoreReadOnlyProperties` | `false` | Skips get-only properties when writing. |
| `DefaultIgnoreCondition` | `Never` | `Never`, `WhenWritingNull`, `WhenWritingDefault`, `Always`, `WhenWriting`, `WhenReading`. |

### Output shape

| Option | Default | Description |
| --- | --- | --- |
| `WriteIndented` | `true` | Writes block collections indented by `IndentSize`. When disabled, collections use the flow style and the document stays on a single line. |
| `IndentSize` | `2` | Number of spaces per indentation level. Ignored when `WriteIndented` is disabled. |
| `IndentBlockSequences` | `true` | Indents a block sequence that is the value of a mapping key. When disabled, the sequence dashes stay at the indentation of the parent mapping. |
| `MappingOrder` | `Declaration` | `Declaration` or `Sorted`. |
| `BlockSequenceMappingStyle` | `Compact` | How a mapping inside a block sequence is emitted. |
| `BlockSequenceSequenceStyle` | `Expanded` | How a nested sequence inside a block sequence is emitted. |
| `ScalarStylePreferences` | `PreferPlainStyle = true`, `PreferQuotedForAmbiguousScalars = true`, `StringStyle = Any` | Quotes scalars that would otherwise resolve to a boolean, a number, or null, and selects the style used for string values. |

`BlockSequenceMappingStyle` controls whether the first key of a mapping shares the line of its sequence dash:

```yaml
# Compact
items:
  - id: 1
    name: first

# Expanded
items:
  -
    id: 1
    name: first
```

`IndentBlockSequences` controls whether a sequence used as a mapping value gets its own indentation level:

```yaml
# IndentBlockSequences = true
product:
  tags:
    - new
    - sale

# IndentBlockSequences = false
product:
  tags:
  - new
  - sale
```

A sequence nested inside another sequence is always indented past its parent dash, because YAML has no unindented form for it.

Block YAML expresses nesting through indentation, so `WriteIndented = false` switches collections to the flow style instead of writing block collections without indentation. The whole document stays on a single line, and `IndentSize`, `BlockSequenceMappingStyle`, and `BlockSequenceSequenceStyle` no longer apply:

```yaml
# WriteIndented = true, IndentSize = 2
product:
  name: Table
  tags:
    - new
```

```yaml
# WriteIndented = false
{product: {name: Table, tags: [new]}}
```

Flow output is read back by the deserializer like any other YAML, and scalars containing a flow indicator such as `,`, `:`, `[`, or `{` are quoted so the round-trip stays faithful.

`StringStyle` selects the style used for string values. The default, `Any`, lets the writer choose between the plain
and the double-quoted style. `Literal` writes a block scalar instead:

```csharp
var options = new YamlSerializerOptions
{
    ScalarStylePreferences = new YamlScalarStylePreferences { StringStyle = ScalarStyle.Literal },
};

YamlSerializer.Serialize(new { Script = "echo one\necho two\n" }, options);
```

```yaml
Script: |
  echo one
  echo two
```

The chomping indicator follows the value: `|-` when it has no trailing line break, `|` for one, and `|+` for more.
`Folded` writes `>` instead, and `Plain`, `SingleQuoted`, and `DoubleQuoted` ask for those styles.

The requested style is used only when the value round-trips through it. A block scalar cannot represent an empty
value, a carriage return, a control character, a line that ends with a blank, or a line that starts with a tab, and
none can be written inside a flow collection; a folded scalar additionally cannot hold a line that starts with a
space. When the style does not fit, the value falls back to the automatic style. The style applies to string values
only — mapping keys and non-string scalars such as numbers, booleans, and dates keep their own representation.

`[YamlStringStyle]` overrides the style for one member and for the strings below it:

```csharp
internal sealed class Job
{
    [YamlStringStyle(ScalarStyle.Literal)]
    public string? Script { get; set; }

    public string? Name { get; set; }
}
```

```yaml
Script: |-
  echo one
  echo two
Name: build
```

`PreferQuotedForAmbiguousScalars` keeps a round-trip faithful when a string looks like another type:

```csharp
YamlSerializer.Serialize(new { A = "yes", B = "123", C = "plain" });
```

```yaml
A: yes
B: "123"
C: plain
```

### Reading

| Option | Default | Description |
| --- | --- | --- |
| `UnmappedMemberHandling` | `Skip` | `Disallow` throws when a mapping key matches no member. |
| `RejectUnmatchedProperties` | `false` | Same effect as `UnmappedMemberHandling.Disallow`. |
| `RespectRequiredConstructorParameters` | `true` | Requires a value for every non-optional constructor parameter. |
| `RespectNullableAnnotations` | `true` | Rejects `null` for a member declared non-nullable. |
| `PreferredObjectCreationHandling` | `Replace` | `Populate` adds to the collection or object already held by the member instead of replacing it. |
| `DuplicateKeyHandling` | `Error` | `FirstWins` or `LastWins` accept duplicate mapping keys. |
| `SourceName` | `null` | Name (typically a file path) used to annotate exception messages. |

### Metadata and extensibility

| Option | Default | Description |
| --- | --- | --- |
| `Converters` | empty | Custom converters, evaluated in order, ahead of built-in converters. |
| `TypeClassifiers` | empty | Factories selecting a union case or a derived type from the payload. |
| `TypeInfoResolver` | `null` | Metadata source; set it to a generated `YamlSerializerContext` to avoid reflection. |
| `PolymorphismOptions` | see [Polymorphism](#polymorphism) | Discriminator style, property name, unknown-type handling, runtime mappings. |
| `ReferenceHandling` | `None` | See [Anchors, aliases, and merge keys](#anchors-aliases-and-merge-keys). |
| `Schema` / `UseSchema` | `Core` / `false` | See [Schemas](#schemas). |

`GetTypeInfo<T>()` and `TryGetTypeInfo<T>()` return the contract metadata an options instance resolves for a type.

## Attributes

| Attribute | Target | Description |
| --- | --- | --- |
| `YamlPropertyNameAttribute` | member | Sets the YAML key. Takes precedence over every naming policy. |
| `YamlNamingPolicyAttribute` | type, member | Overrides `PropertyNamingPolicy`. |
| `YamlPropertyOrderAttribute` | member | Orders emitted keys. |
| `YamlIgnoreAttribute` | type, member | Overrides `DefaultIgnoreCondition`. |
| `YamlIncludeAttribute` | member | Includes a non-public or otherwise excluded member. |
| `YamlRequiredAttribute` | member | Requires the key to be present when reading. |
| `YamlConstructorAttribute` | constructor | Selects the deserialization constructor, including a non-public one. |
| `YamlConverterAttribute` | type, member | Uses a custom converter. |
| `YamlExtensionDataAttribute` | member | Collects unmatched mapping keys. |
| `YamlEnumMemberNameAttribute` | enum member | Sets the YAML value of an enum member. |
| `YamlNumberHandlingAttribute` | type, member | `AllowReadingFromString`, `WriteAsString`, `AllowNamedFloatingPointLiterals`. |
| `YamlObjectCreationHandlingAttribute` | type, member | `Replace` or `Populate`. |
| `YamlUnmappedMemberHandlingAttribute` | type | `Skip` or `Disallow` for this type. |
| `YamlBlockSequenceItemStyleAttribute` | type, member | Block sequence item style for this type or member. |
| `YamlPolymorphicAttribute` | type | Enables and configures polymorphism. |
| `YamlDerivedTypeAttribute` | type | Registers a derived type on its base type. |
| `YamlDerivedTypeMappingAttribute` | assembly, type | Registers a derived type from outside the base type's assembly. |
| `YamlSerializableAttribute` | context | Declares a root type for source generation. |
| `YamlSourceGenerationOptionsAttribute` | context | Configures a generated context. |

### Naming policies

`YamlNamingPolicyAttribute` overrides `YamlSerializerOptions.PropertyNamingPolicy`. On a type it applies to every
member; on a member it applies to that member only. `YamlPropertyNameAttribute` takes precedence over both.

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

### Ignoring members

`YamlIgnoreAttribute` overrides `YamlSerializerOptions.DefaultIgnoreCondition`. On a type it sets the default condition
for every property and field; on a member it applies to that member only.

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

### Ordering members

```csharp
internal sealed class Product
{
    [YamlPropertyOrder(1)]
    public int Id { get; set; }

    [YamlPropertyOrder(2)]
    public string? Name { get; set; }
}
```

### Enum member names

```csharp
internal enum Level
{
    Low,

    [YamlEnumMemberName("very-high")]
    VeryHigh,                                         // very-high
}
```

### Populating existing values

`YamlObjectCreationHandling.Populate` adds to the value a member already holds instead of replacing it. It applies to
collections and to nested objects.

```csharp
internal sealed class Settings
{
    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    public List<string> Tags { get; set; } = ["default"];
}

// Tags: ["default", "extra"]
YamlSerializer.Deserialize<Settings>("Tags:\n  - extra\n");
```

### Extension data

`YamlExtensionDataAttribute` collects the mapping keys that match no member. The annotated member must be a
`YamlMapping`, or a dictionary whose keys are `string` and whose values are `object` or `YamlNode`:

```csharp
internal sealed class Product
{
    public int Id { get; set; }

    [YamlExtensionData]
    public IReadOnlyDictionary<string, object?>? Extra { get; set; }
}
```

The supported dictionary types are `Dictionary<string, TValue>`, `IDictionary<string, TValue>`, and
`IReadOnlyDictionary<string, TValue>`. A read-only dictionary cannot be mutated through its declared type, so the
deserializer creates a `Dictionary<string, TValue>`, copies the entries of the current value into it, and assigns it to
the member. The member must be settable, unless its value is already a `Dictionary<string, TValue>` which is then
updated in place.

## Lifecycle callbacks

Implement any of these interfaces to run code around serialization:

```csharp
internal sealed class Product : IYamlOnSerializing, IYamlOnSerialized, IYamlOnDeserializing, IYamlOnDeserialized
{
    public void OnSerializing() { }
    public void OnSerialized() { }
    public void OnDeserializing() { }
    public void OnDeserialized() { }
}
```

An exception thrown from a callback is wrapped in a `YamlException`.

## Custom converters

Custom converters derive from `YamlConverter<T>` and are registered through `YamlSerializerOptions.Converters`,
`YamlSourceGenerationOptionsAttribute.Converters`, or `YamlConverterAttribute`.

```csharp
internal sealed class UriConverter : YamlConverter<Uri>
{
    public override Uri Read(YamlReader reader) => new(reader.GetScalarValue(), UriKind.RelativeOrAbsolute);

    public override void Write(YamlWriter writer, Uri value) => writer.WriteString(value.ToString());
}

var options = new YamlSerializerOptions { Converters = [new UriConverter()] };
```

`YamlReader` walks the payload token by token (`TokenType`, `Read()`, `Skip()`, `GetScalarValue()`, `Tag`, `Anchor`,
`Alias`), and `YamlWriter` emits it (`WriteStartMapping()`, `WritePropertyName()`, `WriteScalar()` overloads for every
built-in type, `WriteTag()`, `WriteAnchor()`, `WriteAlias()`). The `YamlScalar` static class exposes the span-based
scalar parsers used by the built-in converters (`TryParseInt64`, `TryParseDouble`, `IsNull`, `ResolveObject`, …).

Override `CanPopulate` and `Populate` to support `YamlObjectCreationHandling.Populate`.

Derive from `YamlConverterFactory` to create converters dynamically for a family of types.

`YamlConverterAttribute` also accepts an open generic converter type when the annotated type, or the type of the
annotated member, is a generic type with the same number of generic parameters. The converter is closed over the type
arguments of the target type.

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

## Polymorphism

Annotate a base type with `YamlPolymorphicAttribute` and register its derived types with `YamlDerivedTypeAttribute`.

```csharp
[YamlPolymorphic]
[YamlDerivedType(typeof(Dog), "dog")]
[YamlDerivedType(typeof(Cat), "cat")]
internal abstract class Animal
{
    public string Name { get; set; } = "";
}
```

```yaml
$type: dog
Name: Rex
```

| Setting | Values | Description |
| --- | --- | --- |
| `DiscriminatorStyle` | `Property` (default), `Tag`, `Both` | Whether the type is identified by a mapping key or by a YAML tag. |
| `TypeDiscriminatorPropertyName` | `$type` | Key used by the `Property` style. |
| `UnknownDerivedTypeHandling` | `Fail` (default), `FallBackToBase` | Behavior when the discriminator matches no registration. |
| `InferClosedTypePolymorphism` | `false` | See [Closed hierarchies](#closed-hierarchies). |

Discriminators can be strings or integers. With the `Tag` style, the discriminator is a YAML tag:

```csharp
[YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag)]
[YamlDerivedType(typeof(Dog), Tag = "!dog")]
internal abstract class Animal;
```

```yaml
!dog
Name: Rex
```

The same settings are available on `YamlSerializerOptions.PolymorphismOptions` for the whole serializer.

`UnknownDerivedTypeHandling` applies while reading. While writing, the runtime type must match a registration exactly:
serializing a subclass of a registered derived type that is not registered itself throws `NotSupportedException` rather
than writing it under the discriminator of its closest registered base, which would silently drop the members it
declares. This holds for both the reflection-based serializer and a source-generated `YamlSerializerContext`.

### Registering derived types at runtime

`PolymorphismOptions.DerivedTypeMappings` registers derived types without touching the base type, which enables
polymorphism across assemblies (plugin systems, clean architecture). Runtime entries are merged with attribute-based
ones, and attributes win on conflicts.

```csharp
var options = new YamlSerializerOptions
{
    PolymorphismOptions = new YamlPolymorphismOptions
    {
        DerivedTypeMappings =
        {
            [typeof(Animal)] = [new YamlDerivedType(typeof(Dog), "dog")],
        },
    },
};
```

`YamlDerivedTypeMappingAttribute` does the same declaratively, and is the form the source generator understands.

### Open generic derived types

`YamlDerivedTypeAttribute` accepts an open generic derived type on a generic base type. The derived type is closed for
each instantiation of the base type by matching the base type it declares, so a single attribute covers every
instantiation.

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

A `closed` class hierarchy already lists its derived types in metadata, so they do not need to be repeated with
`YamlDerivedTypeAttribute`. Set `InferClosedTypePolymorphism` to register every derived type of a closed base type
automatically, using the derived type name, without the generic arity suffix, as its discriminator.

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

Inference is opt-in. It can also be enabled for a single type with `YamlPolymorphicAttribute`, which takes precedence
over the serializer options, so `InferClosedTypePolymorphism = false` excludes a type from a global opt-in:

```csharp
[YamlPolymorphic(InferClosedTypePolymorphism = true)]
public closed class Shape
{
}
```

Explicit registrations replace inference: a type declaring `YamlDerivedTypeAttribute` or a runtime mapping registers
only those derived types. Enabling `YamlPolymorphicAttribute.InferClosedTypePolymorphism` on a type that is not declared
`closed` throws an `InvalidOperationException` and reports the `MFY023` diagnostic. A derived type that is itself
`closed` brings its own hierarchy along: its derived types are registered under the root of the hierarchy too, so the
most derived type of a fully closed hierarchy is the one written and read back. A derived type that is not `closed` ends
the inference, as its own derived types are not known.

A derived type must be at least as visible as the base type it is registered under, and two derived types cannot share a
name. Reflection-based serialization throws an `InvalidOperationException` in those cases, and the source generator skips
the derived type and reports the `MFY025` diagnostic.

## Merge keys

A merge key (`<<`) copies the entries of another mapping into the current one. Its value can be an inline mapping, an alias to an anchored mapping, or a sequence mixing both. Keys written in the mapping itself always win over merged keys, and in a merge sequence the last entry wins:

```yaml
defaults: &defaults
  timeout: 30
  retries: 2

prod:
  <<: *defaults
  timeout: 60   # wins over the merged value
```

Merge keys are part of the `Core` and `Extended` schemas; they are plain keys for `YamlSchemaKind.Json`. An alias as the merge value requires `ReferenceHandling = YamlReferenceHandling.Preserve`, as aliases are only resolved in that mode:

```csharp
var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };
var config = YamlSerializer.Deserialize<Config>(yaml, options);
```

An alias resolves to the value the anchored node was deserialized into. When that value is an object, every readable member takes part in the merge, including the members the anchored mapping left out. Source-generated deserialization does not support aliases as merge values.

## C# unions

A C# union is serialized as its selected case, without a wrapper:

```csharp
internal union Setting(bool, int, string);

YamlSerializer.Serialize(new Setting(42)); // 42
```

Cases are selected by YAML shape when deserializing, so a union whose cases use distinct shapes needs no configuration.
A payload matching several cases &mdash; two cases that both serialize as a mapping, for instance &mdash; fails unless a
type classifier is registered. `YamlUnionTypeStructuralClassifier` tells mapping cases apart by their keys:

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

The classifier starts with every mapping case as a candidate and eliminates the cases that do not declare a key present
in the payload. Keys no case declares eliminate only the cases that reject unmapped members, and a case missing a
required key is eliminated once the mapping has been read. Deserialization succeeds when exactly one candidate remains.
A union declaring cases that can never be told apart is rejected when the classifier is created.

A union declaring a nullable case round-trips `null` to that case. When no case is nullable, `null` produces the default
value of the union.

## Type classifiers

Implement `YamlTypeClassifierFactory` to select a union case, or a derived type, with your own rules. `CanClassify`
receives a context whose `Kind` is either `YamlTypeClassifierKind.Union` or `YamlTypeClassifierKind.PolymorphicType`:

```csharp
internal sealed class ShapeClassifier : YamlTypeClassifierFactory
{
    public override bool CanClassify(YamlTypeClassifierContext context)
        => context.Kind is YamlTypeClassifierKind.PolymorphicType && context.DeclaringType == typeof(Shape);

    public override YamlTypeClassifier CreateYamlClassifier(YamlTypeClassifierContext context, YamlSerializerOptions options)
        => reader => /* inspect the payload and return a type from context.DerivedTypes */;
}
```

The classifier reads a private copy of the value, so it may consume the reader. It never overrides an explicit
discriminator or tag: it runs only once those have failed to resolve a type, and returning `null` falls back to the
default derived type or the usual failure.

`YamlTypeClassification.Classify` and `YamlTypeClassification.ClassifyBufferedNode` expose the buffering logic used
internally, so a classifier can inspect a node and let the deserializer replay it afterwards.

## Anchors, aliases, and merge keys

The parser, the syntax tree, and the DOM handle anchors (`&name`), aliases (`*name`), and merge keys (`<<`) natively.
The serializer applies extra rules.

### Anchors

Declaring an anchor is always allowed when reading. Set `AllowAnchors = false` to reject any document that declares one.

### Aliases

Resolving an alias requires object reference tracking, so `ReferenceHandling` must be `Preserve` or `PreserveMinimal`:

```csharp
var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

var config = YamlSerializer.Deserialize<Config>("""
    First: &shared
      Timeout: 30
    Second: *shared
    """, options);

// ReferenceEquals(config.First, config.Second) is true
```

Without it, an alias throws `YamlException: Aliases are not supported when deserializing into '…' unless
ReferenceHandling is Preserve`. Set `AllowAliases = false` to reject aliases outright.

When writing, `ReferenceHandling` decides whether shared objects become anchors and aliases:

| Value | Behavior |
| --- | --- |
| `None` (default) | References are expanded. A cycle exceeds `MaxDepth` and throws. |
| `Preserve` | Every object gets an anchor; repeated references become aliases. Cycles are supported. |
| `PreserveMinimal` | A pre-serialization pass identifies shared and cyclic references, and only those get an anchor. |

```csharp
var shared = new Section { Timeout = 1 };
YamlSerializer.Serialize(new Config { First = shared, Second = shared },
    new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.PreserveMinimal });
```

```yaml
First: &id001
  Timeout: 1
Second: *id001
```

### Merge keys

A merge key whose value is a mapping, or a sequence of mappings, is applied when reading. Later keys win over merged
ones, and earlier entries of a merge sequence win over later ones.

```csharp
YamlSerializer.Deserialize<Section>("""
    <<: { Timeout: 30, Retries: 2 }
    Timeout: 60
    """);
// Timeout = 60, Retries = 2
```

Merge keys are only recognized under the `Core` and `Extended` schemas; the `Json` and `Failsafe` schemas treat `<<` as
an ordinary key. A merge key whose value is an alias additionally requires `ReferenceHandling` to be `Preserve` or
`PreserveMinimal`, and is currently supported when binding to dictionaries only.

## Schemas

`YamlSchemaKind` selects the rules used to resolve untagged scalars:

| Kind | Description |
| --- | --- |
| `Failsafe` | YAML 1.2 §10.1. Every plain scalar is a string. |
| `Json` | YAML 1.2 §10.2. JSON-compatible resolution. |
| `Core` | YAML 1.2 §10.3. The default; adds `~`, `Null`, `TRUE`, hexadecimal and octal integers, `.inf`, `.nan`. |
| `Extended` | Adds `y`/`yes`/`on` booleans, `!!timestamp`, `!!merge`, and `_` digit separators. |

Scalar resolution goes through the selected schema only when `UseSchema = true`. Otherwise built-in converters use a
faster span-based YAML 1.2 path, which still honors quoted scalars as strings.

```csharp
var options = new YamlSerializerOptions { UseSchema = true, Schema = YamlSchemaKind.Extended };
YamlSerializer.Deserialize<Dictionary<string, object>>("enabled: yes", options); // true, not "yes"
```

The schema types (`FailsafeSchema`, `JsonSchema`, `CoreSchema`, `ExtendedSchema`) are public, derive from `SchemaBase`,
and implement `IYamlSchema`. They expose tag expansion and shortening, scalar rules, and tag registration, so a custom
schema can be built by deriving from one of them and overriding `PrepareScalarRules`.

## Source generation

Declare a partial context derived from `YamlSerializerContext` and annotate each root type with
`YamlSerializableAttribute`.

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

The context itself can be passed to the serializer:

```csharp
var yaml = YamlSerializer.Serialize(product, AppYamlContext.Default);
var copy = YamlSerializer.Deserialize<Product>(yaml, AppYamlContext.Default);
```

`YamlSourceGenerationOptionsAttribute` mirrors most of `YamlSerializerOptions`, including `Converters`. A context can
also be constructed with a `YamlSerializerOptions` instance, and `CreateOptions` derives an options instance that keeps
the context as its resolver:

```csharp
var options = AppYamlContext.Default.CreateOptions(o => o with { SourceName = "config.yaml" });
```

`YamlSerializableAttribute.TypeInfoPropertyName` renames the generated property when the default name collides.

Non-public members annotated with `YamlIncludeAttribute` and non-public constructors annotated with
`YamlConstructorAttribute` are supported: the generated context reaches them through `UnsafeAccessorAttribute`, which
NativeAOT and trimming understand.

### Diagnostics

| Id | Severity | Description |
| --- | --- | --- |
| `MFY001` | Error | The context type must be declared `partial`. |
| `MFY002` | Error | A member uses a type the generator cannot serialize. |
| `MFY003` | Error | An extension data member uses an unsupported type. |
| `MFY004` | Error | A type declares several `[YamlExtensionData]` members. |
| `MFY005` | Error | A `[YamlSourceGenerationOptions]` value is invalid. |
| `MFY006` | Error | A converter type is invalid. |
| `MFY020` | Error | `[YamlDerivedTypeMapping]` declares a type that is not assignable to the base type. |
| `MFY021` | Warning | The base type of a `[YamlDerivedTypeMapping]` has no `[YamlPolymorphic]`; serializer defaults are used. |
| `MFY022` | Warning | An open generic derived type cannot be closed for the base type and is ignored. |
| `MFY023` | Error | `InferClosedTypePolymorphism` is enabled on a type that is not `closed`. |
| `MFY024` | Warning | Inference is replaced by the explicit `[YamlDerivedType]` registrations of the type. |
| `MFY025` | Warning | An inferred derived type is ignored (visibility, or duplicate discriminator). |

## NativeAOT and trimming

The package is annotated `IsAotCompatible` and `IsTrimmable`. Source generation avoids reflection-based metadata
discovery and is the preferred mode for NativeAOT and trimming-sensitive applications.

Reflection-based serialization can be disabled entirely for applications that only use source-generated metadata, via
the `MeziantouFrameworkYamlIsReflectionEnabledByDefault` MSBuild property:

```xml
<PropertyGroup>
  <MeziantouFrameworkYamlIsReflectionEnabledByDefault>false</MeziantouFrameworkYamlIsReflectionEnabledByDefault>
</PropertyGroup>
```

The property is published as a runtime host configuration option and as a trimmer feature switch, so the reflection code
paths are removed from the trimmed output. It defaults to `false` when `PublishAot` or `NativeAot` is `true`; set it
explicitly to override that. `YamlSerializer.IsReflectionEnabledByDefault` reports the effective value at runtime.

When reflection is disabled, use source-generated `YamlSerializerContext` metadata for typed serialization and
deserialization.

## Document Object Model

Use the DOM when you need to inspect or transform YAML without binding to a CLR type.

```csharp
using Meziantou.Framework.Yaml.Model;

var stream = YamlStream.Load(new StringReader("""
    product:
      id: 1
      name: Sample product
    """));

var document = stream[0];
var root = (YamlMapping)document.Contents!;
var product = (YamlMapping)root["product"]!;
var name = ((YamlValue)product["name"]!).Value;
```

`YamlStream` is a list of `YamlDocument`, so a multi-document stream is read in one pass. `YamlMapping` implements both
`IDictionary<YamlElement, YamlElement>` and `IList<KeyValuePair<…>>`, so entries keep their document order and can be
inserted at a given index. `YamlMapping` and `YamlSequence` expose `Style` (`Block` or `Flow`), and `YamlElement`
exposes `Anchor` and `Tag`.

Every node supports `DeepClone()`, `WriteTo(TextWriter)`, `EnumerateEvents()`, and bridges to the serializer:

```csharp
var node = YamlNode.FromObject(product);
var back = node.ToObject<Product>();
```

A `YamlNode` can also be used directly as a member type, which is how `[YamlExtensionData]` keeps unmatched content in
its original form.

## Syntax tree

`YamlSyntaxTree` produces a lossless representation: with `IncludeTrivia`, comments and whitespace are preserved and
`ToFullString()` returns the original text byte for byte. Every node and token carries a `Span` and a `FullSpan`.

```csharp
using Meziantou.Framework.Yaml.Syntax;

var yaml = """
    # deployment settings
    replicas: 3 # keep in sync with the chart
    """;

var tree = YamlSyntaxTree.Parse(yaml, new YamlSyntaxOptions { IncludeTrivia = true });

foreach (var token in tree.Tokens)
{
    Console.WriteLine($"{token.Kind} {token.Span} {token.Text}");
}

Console.WriteLine(tree.ToFullString() == yaml); // True
```

This is the layer to use for tooling: linters, formatters, or edits that must not disturb the rest of the file.

## Parser and emitter

The event-based API mirrors libyaml and is available for streaming scenarios.

```csharp
using Meziantou.Framework.Yaml;
using Meziantou.Framework.Yaml.Events;

var parser = Parser.CreateParser(new StringReader(yaml));
var reader = new EventReader(parser);

reader.Expect<StreamStart>();
while (!reader.Accept<StreamEnd>())
{
    reader.Expect<DocumentStart>();
    // Peek, Allow, Accept, Skip(untilDepth) ...
}
```

`Emitter` writes parsing events to a `TextWriter` and accepts an indentation width, a maximum line width, and a
canonical mode.

## Error handling

Every parse and binding failure derives from `YamlException`, which carries the `Start` and `End` marks (line, column,
character index) of the offending node. `SyntaxErrorException` and `SemanticErrorException` cover scanning and parsing
failures.

Set `SourceName` to prefix messages with the origin of the document:

```csharp
var options = new YamlSerializerOptions { SourceName = "config.yaml" };
YamlSerializer.Deserialize<Config>(yaml, options);
// config.yaml: (Lin: 0, Col: 3, Chr: 3) - ...: Expected a Scalar token but found 'StartSequence'.
```

Use `TryDeserialize` when a failure is expected and an exception is not wanted.

## Hardening untrusted input

| Option | Default | Purpose |
| --- | --- | --- |
| `MaxDepth` | `64` | Caps the nesting depth of mappings and sequences while reading and writing. `0` means the default. |
| `AllowAnchors` | `true` | Rejects documents declaring anchors. |
| `AllowAliases` | `true` | Rejects documents using aliases, which prevents alias-expansion amplification. |
| `MaxAliasExpansionNodeCount` | `100000` | Caps how many nodes aliases may materialize when building a `YamlNode` model. `0` means the default. |
| `DuplicateKeyHandling` | `Error` | Rejects mappings with duplicate keys. |
| `UnsafeAllowDeserializeFromTagTypeName` | `false` | Allows a YAML tag to name a CLR type to instantiate. Enable it only for trusted input. |

```csharp
var options = new YamlSerializerOptions
{
    MaxDepth = 32,
    AllowAnchors = false,
    AllowAliases = false,
    UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow,
};
```

The document object model expands each alias into a copy of the anchored subtree, so nesting anchors makes the node
count grow exponentially while the document grows linearly. `MaxDepth` does not bound this, because the growth is in
breadth rather than depth. `MaxAliasExpansionNodeCount` bounds it directly, and only counts nodes produced by alias
expansion, so documents that do not use aliases are never affected.

Pass the options to `YamlStream.Load` when reading untrusted input into the model; the parameterless overload uses
`YamlSerializerOptions.Default`:

```csharp
var stream = YamlStream.Load(reader, options);
```
