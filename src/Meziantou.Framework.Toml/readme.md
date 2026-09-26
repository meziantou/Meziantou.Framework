# Meziantou.Framework.Toml

`Meziantou.Framework.Toml` is a [TOML 1.1](https://toml.io/en/v1.1.0) parser, round-trippable syntax tree, and
`System.Text.Json`-style serializer for .NET. It serializes object graphs, deserializes typed models or an untyped
document model, and generates serialization metadata at compile time for NativeAOT and trimming scenarios.

The source generator ships inside the package. No additional package is required to use generated
`TomlSerializerContext` types.

The library is based on [Tomlyn](https://github.com/xoofx/Tomlyn) by Alexandre Mutel. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for its license.

## Install the package

```bash
dotnet add package Meziantou.Framework.Toml
```

## Table of contents

- [Overview](#overview)
- [Serialize and deserialize objects](#serialize-and-deserialize-objects)
- [Supported types](#supported-types)
- [Configure serialization](#configure-serialization)
- [Attributes](#attributes)
- [Lifecycle callbacks](#lifecycle-callbacks)
- [Custom converters](#custom-converters)
- [Extension data](#extension-data)
- [Polymorphism](#polymorphism)
- [Source generation](#source-generation)
- [NativeAOT and trimming](#nativeaot-and-trimming)
- [Document Object Model](#document-object-model)
- [Syntax tree](#syntax-tree)
- [Lexer and parser](#lexer-and-parser)
- [Error handling](#error-handling)

## Overview

The package targets **TOML 1.1**. There is no TOML 1.0 mode: documents are always read and validated with the TOML 1.1
rules.

Values are read into .NET types, which rejects a few documents that TOML accepts. Every parser reports them as errors:

- A float that overflows a 64-bit `double`, such as `1e400`, is an error rather than infinity.
- A leap second (`23:59:60`), the year `0000`, an offset further from UTC than ±14:00, and an offset date-time whose
  UTC instant is before the year 1 or after 9999 are errors, because `DateTime` and `DateTimeOffset` cannot hold them.
  The message says which limit applies.

| Namespace | Content |
| --- | --- |
| `Meziantou.Framework.Toml` | `TomlSerializer`, `TomlSerializerOptions`, `TomlTypeInfo<T>`, `TomlDateTime`, `TomlException` |
| `Meziantou.Framework.Toml.Serialization` | Attributes, `TomlSerializerContext`, converters, `TomlReader`, `TomlWriter`, metadata store |
| `Meziantou.Framework.Toml.Model` | Document Object Model: `TomlTable`, `TomlArray`, `TomlTableArray` |
| `Meziantou.Framework.Toml.Parsing` | `TomlLexer`, `TomlParser`, `SyntaxParser` |
| `Meziantou.Framework.Toml.Syntax` | Lossless syntax tree: `DocumentSyntax`, `KeyValueSyntax`, `SyntaxVisitor`, diagnostics |

The serializer follows the `System.Text.Json` API shape, with its own attributes: `System.Text.Json.Serialization`
attributes, such as `[JsonPropertyName]`, are not recognized.

## Serialize and deserialize objects

```csharp
using Meziantou.Framework.Toml;

var toml = TomlSerializer.Serialize(new Person("Ada", 37));
var person = TomlSerializer.Deserialize<Person>(toml);

public sealed record Person(string Name, int Age);
```

`TomlSerializer` provides overloads for `string`, `Stream` (UTF-8), `TextReader`, and `TextWriter`:

```csharp
using var stream = File.OpenRead("config.toml");
var config = TomlSerializer.Deserialize<ServerConfig>(stream);

using var writer = new StreamWriter("output.toml");
TomlSerializer.Serialize(writer, config);
```

`DeserializeAsync` and `SerializeAsync` read and write a `Stream` asynchronously, for example an ASP.NET Core request or
response body, which rejects synchronous I/O. The document is still parsed and formatted in memory:

```csharp
var config = await TomlSerializer.DeserializeAsync<ServerConfig>(request.Body, cancellationToken: cancellationToken);
await TomlSerializer.SerializeAsync(response.Body, config, cancellationToken: cancellationToken);
```

Set `MaxInputLength` to reject a document that is too long before it is loaded, for example one sent by a user.

`TryDeserialize` returns `false` instead of throwing when the input is not valid TOML or does not match the model. It is
available for every input type and metadata style. An exception thrown by the model itself, such as a setter or a
deserialization callback that validates a value, is not caught, like in `System.Text.Json`; a constructor that throws is
reported as a `TomlException`:

```csharp
if (!TomlSerializer.TryDeserialize<ServerConfig>(toml, out var config))
{
    // Invalid input
}
```

The metadata used to map objects comes from one of two sources:

1. **Source-generated metadata** from a `TomlSerializerContext`. This is the recommended mode for NativeAOT, trimming,
   and hot paths. See [Source generation](#source-generation).
2. **Reflection**, enabled by default. See [NativeAOT and trimming](#nativeaot-and-trimming) to disable it.

## Supported types

| Category | Types |
| --- | --- |
| Boolean | `bool` |
| Numeric | `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `nint`, `nuint`, `float`, `double`, `decimal`, `Half`, `Int128`, `UInt128` |
| Text | `char`, `string` |
| Date/time | `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TomlDateTime` |
| Other | `Guid`, `TimeSpan`, `Uri`, `Version`, enums (as numbers, or as names with [`TomlStringEnumConverter`](#enums-as-strings)) |
| Collections | `T[]`, `List<T>`, `IList<T>`, `IReadOnlyList<T>`, `HashSet<T>`, `SortedSet<T>`, `ISet<T>`, `IReadOnlySet<T>`, `ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>`. Other collections, such as `Queue<T>` or `ConcurrentBag<T>`, are rejected (`TomlException`, or `MFTOML003` in generated code): use a converter. |
| Dictionaries | `Dictionary<string, T>`, `IDictionary<string, T>`, `IReadOnlyDictionary<string, T>`, `SortedDictionary<string, T>`. `ImmutableDictionary<string, T>` can be written but not read. |
| Document Object Model | `TomlTable`, `TomlArray`, `TomlTableArray`, `TomlObject`, `object` |
| Objects | Classes, records, and structs, with property setters or constructor parameters |

A `DateTime` is written according to its `Kind`, and read back with the same kind, like `System.Text.Json`: a UTC value
is an offset date-time with `Z`, a local value an offset date-time with the offset of the machine, and an unspecified
value a local date-time. Reading converts any other numeric offset to local time.

TOML table keys are strings, so dictionaries must use `string` keys. A member typed as `TomlObject` accepts any TOML
container (`TomlTable`, `TomlArray`, or `TomlTableArray`).

Collections of objects are written as arrays of tables:

```csharp
public sealed class Config
{
    public Package[] Package { get; set; } = [];
}

public sealed class Package
{
    public string Name { get; set; } = "";
}
```

```toml
[[Package]]
Name = "core"
[[Package]]
Name = "tools"
```

An empty collection of objects is written as an empty array (`Package = []`), because TOML has no header syntax for an
empty array of tables.

## Configure serialization

`TomlSerializerOptions` is an immutable record. It caches metadata on first use, so create an instance once and reuse it.

```csharp
using Meziantou.Framework.Toml;

var options = new TomlSerializerOptions
{
    PropertyNamingPolicy = TomlNamingPolicy.SnakeCaseLower,
    PreferredObjectCreationHandling = TomlObjectCreationHandling.Replace,
    WriteIndented = true,
    IndentSize = 4,
    MaxDepth = 64,
    DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingNull,
};

var toml = TomlSerializer.Serialize(config, options);
```

| Option | Default | Description |
| --- | --- | --- |
| `PropertyNamingPolicy` | `null` | Naming policy for member names: `TomlNamingPolicy.CamelCase`, `PascalCase`, `SnakeCaseLower`, `SnakeCaseUpper`, `KebabCaseLower`, `KebabCaseUpper`, or a class deriving from `TomlNamingPolicy`. `null` uses the CLR names, like `System.Text.Json`. |
| `DictionaryKeyPolicy` | `null` | Naming policy for dictionary keys when writing. |
| `PropertyNameCaseInsensitive` | `false` | Matches member names case-insensitively when reading. |
| `IncludeFields` | `false` | Includes public fields. Fields with `[TomlInclude]` are always included. |
| `IgnoreReadOnlyFields` | `false` | Skips `readonly` fields when writing. |
| `IgnoreReadOnlyProperties` | `false` | Skips properties without a public setter when writing. A property with an `init` accessor is not read-only. |
| `PreferredObjectCreationHandling` | `Replace` | Replaces or populates object and collection members when reading. |
| `DefaultIgnoreCondition` | `WhenWritingNull` | Skips `null` (or default) values when writing: `Never`, `WhenWritingNull`, or `WhenWritingDefault`. `[TomlIgnore(Condition = ...)]` overrides it for a member, including with `Never`. TOML has no null, so a `null` member that is not skipped throws a `TomlException`. |
| `DuplicateKeyHandling` | `Error` | Behavior when a key is assigned a value twice. `LastWins` keeps the last value; table redefinitions are always rejected. |
| `UnmappedMemberHandling` | `Skip` | Skips (`Skip`) or rejects (`Disallow`) keys that match no member. Extension data still collects them. `[TomlUnmappedMemberHandling]` overrides it for a type. |
| `RespectRequiredConstructorParameters` | `true` | A constructor parameter without a default value must be present. When `false`, it receives the default value of its type. |
| `RespectNullableAnnotations` | `true` | Rejects `null` in members and constructor parameters declared as non-nullable reference types, when reading and writing. The check applies before `DefaultIgnoreCondition`, so such a member throws instead of being skipped, unless the member itself has `[TomlIgnore(Condition = WhenWritingNull)]` or `WhenWritingDefault`. |
| `MaxInputLength` | `0` (no limit) | Maximum length of the input, in characters for a `string` or a `TextReader` and in bytes for a `Stream`. Longer input throws `TomlException`, and a `Stream` is not read further. |
| `MaxDepth` | `0` (64) | Maximum nesting depth of tables and arrays. With a larger value, a document nested deeper than the stack of the current thread allows throws `TomlException` instead of overflowing the stack. |
| `WriteIndented` | `false` | Indents the header and the key/value pairs of a table once for each table it is nested in. |
| `IndentSize` | `2` | Number of spaces per indentation level. |
| `NewLine` | `Lf` | Line ending style (`Lf` or `CrLf`). |
| `MappingOrder` | `OrderThenDeclaration` | Member order: `Declaration`, `Alphabetical`, `OrderThenDeclaration`, or `OrderThenAlphabetical`. Declaration order lists the members of the base types first, then in each type the fields and then the properties. The default honors `[TomlPropertyOrder]`, like `System.Text.Json` honors `[JsonPropertyOrder]`. |
| `DottedKeyHandling` | `Literal` | Writes member names and dictionary keys containing a dot as quoted keys (`Literal`) or expands them into subtables (`Expand`). The keys of a `TomlTable` are always written as they are. Reading is not affected: expanded keys are read back as nested tables, so such a model does not round-trip with `Expand`. |
| `RootValueHandling` | `Error` | Behavior when the root value is not a table. `WrapInRootKey` writes it under `RootValueKeyName` (`"value"`). |
| `InlineTablePolicy` | `Never` | When nested objects are written as inline tables: `Never`, `WhenSmall`, or `Always`. |
| `TableArrayStyle` | `Headers` | Writes arrays of tables as `[[name]]` headers (`Headers`) or as inline arrays of inline tables (`InlineArrayOfTables`). |
| `StringStylePreferences` | Basic strings | Default string style (`Basic`, `Literal`, `MultilineBasic`, `MultilineLiteral`), literal preference, and `AllowHexEscapes` (off by default), which escapes control characters with the TOML 1.1 `\xHH` and `\e` instead of `\uXXXX`. |
| `PolymorphismOptions` | `$type` discriminator | Discriminator property name, unknown derived type handling, and runtime derived type mappings. |
| `Converters` | Empty | Custom converters. They take precedence over the built-in converters. |
| `TypeInfoResolver` | `null` | Metadata resolver, for example a source-generated context. |
| `MetadataStore` | `null` | Captures comments and source locations when reading. See [Metadata and trivia](#metadata-and-trivia). |
| `SourceName` | `null` | File name reported in `TomlException` messages. |

`options.GetTypeInfo<T>()` returns the `TomlTypeInfo<T>` the options resolve for a type, from the converters, the
`TypeInfoResolver`, the built-in types, or reflection. `TryGetTypeInfo<T>()` returns `false` instead of throwing when no
metadata is available.

### Populating existing values

As in `System.Text.Json`, the default `TomlObjectCreationHandling.Replace` assigns new values to writable members and
leaves read-only members untouched. `Populate` reuses the existing object and collection instances; collections are
appended to, not cleared. It can be enabled globally with `PreferredObjectCreationHandling`, on a type, or on a member
with `[TomlObjectCreationHandling]`:

```csharp
[TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
public sealed class ReleaserConfiguration
{
    public List<string> Channels { get; } = ["stable"];
}
```

A member-level `Populate` on a member that cannot be populated (for example a struct without a setter) throws. Type-level
and global preferences are best-effort and leave such members unchanged. Populate does not apply to types deserialized
through a parameterized constructor.

### Single value or array

`[TomlSingleOrArray]` lets a collection member accept either a single value or an array:

```csharp
public sealed class PackagingConfiguration
{
    [TomlSingleOrArray]
    [TomlPropertyName("rid")]
    public List<string> RuntimeIdentifiers { get; } = [];
}
```

Both `rid = "win-x64"` and `rid = ["win-x64", "linux-x64"]` are accepted. Without the attribute, a collection member
requires an array.

### Formatting attributes

Formatting options can be overridden for a member or a type. These attributes only affect how values are written:

```csharp
[TomlMappingOrder(TomlMappingOrderPolicy.Alphabetical)]
[TomlDottedKeyHandling(TomlDottedKeyHandling.Expand)]
public sealed class FormattedConfig
{
    [TomlTableArrayStyle(TomlTableArrayStyle.Headers)]
    public Package[] Package { get; set; } = [];

    [TomlInlineTable(TomlInlineTablePolicy.Always)]
    public Owner Owner { get; set; } = new();

    [TomlStringStyle(TomlStringStyle.Literal, PreferLiteralWhenNoEscapes = TomlBooleanPreference.True)]
    public string Description { get; set; } = "plain text";
}
```

| Attribute | Target | Overrides |
| --- | --- | --- |
| `[TomlTableArrayStyle]` | Collection member | `TableArrayStyle` |
| `[TomlInlineTable]` | Member | `InlineTablePolicy`, for this value only |
| `[TomlStringStyle]` | `string` member | `StringStylePreferences` |
| `[TomlMappingOrder]` | Class or struct | `MappingOrder` |
| `[TomlDottedKeyHandling]` | Class or struct | `DottedKeyHandling`, for the member names of the type |

## Attributes

| Attribute | Description |
| --- | --- |
| `[TomlPropertyName]` | Overrides the key name. |
| `[TomlIgnore]` | Ignores the member, always or conditionally (`WhenWritingNull`, `WhenWritingDefault`). |
| `[TomlInclude]` | Includes a non-public member. |
| `[TomlPropertyOrder]` | Sets the order of the member in the table. |
| `[TomlRequired]` | The key must be present; a missing key throws `TomlException`. The C# `required` modifier is honored too, unless the constructor used has `[SetsRequiredMembers]`. |
| `[TomlConstructor]` | Selects the constructor used when reading. Parameters are matched by name to the members of the type. |
| `[TomlExtensionData]` | Collects unmapped keys. See [Extension data](#extension-data). |
| `[TomlConverter]` | Selects a converter (or a converter factory) for a type or member. |
| `[TomlPolymorphic]` | Enables polymorphism on a base type. |
| `[TomlDerivedType]` | Registers a derived type and its discriminator. |
| `[TomlObjectCreationHandling]` | Replaces or populates a type or member when reading. |
| `[TomlUnmappedMemberHandling]` | Skips or rejects unknown keys for a type. |
| `[TomlSingleOrArray]` | Accepts a single value for a collection member. |
| `[TomlStringEnumMemberName]` | Sets the name `TomlStringEnumConverter` uses for an enum value. |

```csharp
public sealed class DatabaseConfig
{
    [TomlRequired]
    public string Host { get; set; } = "";

    [TomlPropertyName("port_number")]
    public int Port { get; set; }

    [TomlIgnore]
    public string ConnectionString => $"{Host}:{Port}";
}

public sealed class Endpoint
{
    [TomlConstructor]
    public Endpoint(string host, int port)
    {
        Host = host;
        Port = port;
    }

    public string Host { get; }
    public int Port { get; }
}
```

## Lifecycle callbacks

Implement the callback interfaces to run code around serialization:

| Interface | Called |
| --- | --- |
| `ITomlOnSerializing` | Before the object is written. |
| `ITomlOnSerialized` | After the object is written. |
| `ITomlOnDeserializing` | When the instance is created, before its members are set. The constructor arguments are set before. So are the `required` members the source generator sets in an object initializer: those of a struct, of a generic type, or of a type created with a constructor that has parameters. |
| `ITomlOnDeserialized` | After the members are read. |

```csharp
public sealed class ValidatedConfig : ITomlOnDeserialized
{
    public int Port { get; set; } = 8080;

    public void OnTomlDeserialized()
    {
        if (Port is < 1 or > 65535)
            throw new InvalidOperationException($"Invalid port: {Port}");
    }
}
```

## Custom converters

Derive from `TomlConverter<T>`, or from `TomlConverterFactory` for open generic types or types chosen at runtime:

```csharp
using Meziantou.Framework.Toml.Serialization;

public sealed class UpperCaseStringConverter : TomlConverter<string>
{
    public override string Read(TomlReader reader) => reader.GetString().ToUpperInvariant();

    public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value.ToUpperInvariant());
}
```

Register it in the options, with `[TomlConverter]` on a type or member, or on a source-generated context. Like
`System.Text.Json`, a converter for `T` on a member also converts a `T?` member:

```csharp
var options = new TomlSerializerOptions { Converters = [new UpperCaseStringConverter()] };

[TomlSourceGenerationOptions(Converters = [typeof(UpperCaseStringConverter)])]
[TomlSerializable(typeof(ServerConfig))]
internal partial class ConverterContext : TomlSerializerContext;
```

`TomlReader` exposes `Read`, `Skip`, `GetString`, `GetInt64`, `GetDouble`, `GetDecimal`, `GetBoolean`,
`GetTomlDateTime`, `GetRawText`, and `PropertyNameEquals`. `TomlWriter` exposes `WritePropertyName`, the
`Write*Value` methods, and the start and end methods for tables, inline tables, arrays, and arrays of tables.

TOML lets a document define a table in several places: an array of tables can be reopened after another table, and
dotted keys can extend a table after another key. `TomlReader` parses the whole document on the first `Read`, so it
reports syntax errors before any value is created, and it returns each table and array of tables as one block. A
converter reads each key of a table once.

### Enums as strings

Enums are written as numbers. `TomlStringEnumConverter` writes their names instead, and reads names case-insensitively
or numbers. Apply it to an enum or a member with `[TomlConverter]`, or add it to the converters of the options or of a
source-generated context to apply it to every enum. `[TomlStringEnumMemberName]` sets the name of a value, and a flags
value is written as a comma-separated list:

```csharp
[TomlConverter(typeof(TomlStringEnumConverter))]
public enum LogLevel
{
    Information,
    [TomlStringEnumMemberName("warn")]
    Warning,
}

var options = new TomlSerializerOptions { Converters = [new TomlStringEnumConverter()] };
```

## Extension data

`[TomlExtensionData]` collects the keys that do not match a member. The member must be a
dictionary with `string` keys, such as `IDictionary<string, object?>` or `TomlTable`:

```csharp
public sealed class ExtensibleConfig
{
    public string Name { get; set; } = "";

    [TomlExtensionData]
    public IDictionary<string, object?>? Extra { get; set; }
}
```

## Polymorphism

Polymorphism uses a discriminator key, `$type` by default:

```csharp
[TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[TomlDerivedType(typeof(Cat), "cat")]
[TomlDerivedType(typeof(Dog), "dog")]
public abstract class Animal
{
    public string Name { get; set; } = "";
}

public sealed class Cat : Animal
{
    public bool Indoor { get; set; }
}

public sealed class Dog : Animal
{
    public string Breed { get; set; } = "";
}
```

```toml
[[animals]]
kind = "cat"
Name = "Whiskers"
Indoor = true

[[animals]]
kind = "dog"
Name = "Rex"
Breed = "Labrador"
```

- A derived type registered without a discriminator is the default type: it is used when the discriminator is missing
  or unknown, and it is written without a discriminator. System.Text.Json throws on an unknown discriminator instead.
- Integer discriminators (`[TomlDerivedType(typeof(Circle), 1)]`) are written as strings.
- Without a default type, an unknown discriminator throws by default. Set
  `UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.FallBackToBaseType` on the attribute or on
  `TomlPolymorphismOptions` to read it as the base type instead.

### Registering derived types outside the base type

When the base type cannot reference its derived types, for example because they live in another project, register them
in the options (reflection) or on the context (source generation):

```csharp
var options = new TomlSerializerOptions
{
    PolymorphismOptions = new TomlPolymorphismOptions
    {
        TypeDiscriminatorPropertyName = "kind",
        DerivedTypeMappings = new Dictionary<Type, IReadOnlyList<TomlDerivedType>>
        {
            [typeof(Animal)] = [new(typeof(Cat), "cat"), new(typeof(Dog), "dog")],
        },
    },
};

[TomlSerializable(typeof(Animal))]
[TomlDerivedTypeMapping(typeof(Animal), typeof(Cat), "cat")]
[TomlDerivedTypeMapping(typeof(Animal), typeof(Dog), "dog")]
internal partial class AnimalContext : TomlSerializerContext;
```

Registrations are merged. `[TomlDerivedType]` on the base type wins over the context mappings, then over the options
mappings.

## Source generation

Declare a `partial` class deriving from `TomlSerializerContext`, with a `[TomlSerializable]` attribute for each root
type. Types reachable from a root are discovered automatically.

```csharp
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Serialization;

[TomlSourceGenerationOptions(PropertyNamingPolicy = TomlKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(ServerConfig))]
internal partial class ServerContext : TomlSerializerContext;

public sealed class ServerConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8080;
}

var toml = TomlSerializer.Serialize(config, ServerContext.Default.ServerConfig);
var roundTrip = TomlSerializer.Deserialize(toml, ServerContext.Default.ServerConfig);

// APIs taking a Type accept a context
var value = TomlSerializer.Deserialize(toml, typeof(ServerConfig), ServerContext.Default);
```

- The generator creates a `Default` instance and one `TomlTypeInfo<T>` property per root.
  `[TomlSerializable(typeof(T), TypeInfoPropertyName = "...")]` renames the property. When two types have the same
  name, or a type is named like a member of the context, the property uses the namespace-qualified name of the type
  (for example `B_Item`).
- `[TomlSourceGenerationOptions]` sets the options at build time. Member names, the members to serialize
  (`IncludeFields`, `IgnoreReadOnlyFields`, `IgnoreReadOnlyProperties`), and the validation (`UnmappedMemberHandling`,
  `RespectRequiredConstructorParameters`, `RespectNullableAnnotations`) are computed when building, so the naming policy
  is not called at runtime.
- `init` and `required` members are supported. A class is created with an `[UnsafeAccessor]` to its constructor, so its
  members keep their initial values and can be populated. A struct, a generic type, or a type created with a constructor
  that has parameters is created with an object initializer instead: its `required` members are replaced, and set to
  `default` when they are not read.
- `[TomlConverter]` on a type or member is supported, including converter factories. The
  converter type must be accessible from the context: public, or internal to the same assembly. A member with a converter is always replaced, never populated.
- Converters of `[TomlSourceGenerationOptions(Converters = [...])]` are resolved at build time, so converter factories
  listed there are not used (`MFTOML012`). Apply `[TomlConverter]` to the type or member instead.

### Diagnostics

| Id | Severity | Description |
| --- | --- | --- |
| `MFTOML001` | Error | The context type, and the types that contain it, must be declared `partial`. |
| `MFTOML002` | Error | A converter type is invalid. |
| `MFTOML003` | Error | A member uses a type the generator cannot serialize. |
| `MFTOML004` | Error | A dictionary member uses non-string keys. |
| `MFTOML005` | Error | A `[TomlSourceGenerationOptions]` value is invalid, or a `[TomlSerializable]` `TypeInfoPropertyName` is not a valid identifier, is used for two types, or clashes with a member of the context or with a member generated for another name (`_Name`, `CreateName`). |
| `MFTOML006` | Error | An extension data member is invalid. |
| `MFTOML007` | Error | A polymorphism configuration is invalid. |
| `MFTOML009` | Error | A `[TomlDerivedTypeMapping]` is invalid. |
| `MFTOML010` | Warning | The base type of a `[TomlDerivedTypeMapping]` has no polymorphic configuration; serializer defaults are used. |
| `MFTOML011` | Error | A TOML attribute is used on a member it does not apply to. |
| `MFTOML012` | Warning | A converter factory in `[TomlSourceGenerationOptions(Converters)]` is not used by generated code. |
| `MFTOML013` | Error | A constructor annotated with `[TomlConstructor]` is private or protected. |
| `MFTOML014` | Error | The context type is generic. It can be nested in a generic type. |
| `MFTOML015` | Error | A type the generated code uses is not accessible from the context, is less accessible than the context, or is file-local. The context has a public property for each type, so every type must be accessible wherever the context is: for example, an `internal` type cannot be used by a `protected` nested context, which derived types in other assemblies can access. |
| `MFTOML016` | Error | A member is a `ref struct`, a delegate, or a pointer, which cannot be serialized. |
| `MFTOML017` | Error | A type registered with `[TomlSerializable]` cannot be serialized: an abstract class or an interface without polymorphism configuration, an open generic type, a multi-dimensional array, a delegate, a ref struct, or a pointer. |
| `MFTOML018` | Error | A TOML attribute has an undefined enum value, such as `[TomlIgnore(Condition = (TomlIgnoreCondition)42)]`. The attribute throws an `ArgumentOutOfRangeException` at run time. |

## NativeAOT and trimming

The package is annotated `IsAotCompatible` and `IsTrimmable`. Source generation avoids reflection-based metadata
discovery and is the preferred mode for NativeAOT and trimming-sensitive applications.

Reflection-based serialization can be disabled entirely for applications that only use source-generated metadata, via
the `MeziantouFrameworkTomlIsReflectionEnabledByDefault` MSBuild property:

```xml
<PropertyGroup>
  <MeziantouFrameworkTomlIsReflectionEnabledByDefault>false</MeziantouFrameworkTomlIsReflectionEnabledByDefault>
</PropertyGroup>
```

The property is published as the `Meziantou.Framework.Toml.TomlSerializer.IsReflectionEnabledByDefault` runtime host
configuration option and as a trimmer feature switch, so the reflection code paths are removed from the trimmed output.
It defaults to `false` when `PublishAot` or `NativeAot` is `true`; set it explicitly to override that. The switch can also
be set with `AppContext.SetSwitch` before the first use of the serializer. `TomlSerializer.IsReflectionEnabledByDefault`
reports the effective value at runtime.

When reflection is disabled, use source-generated `TomlSerializerContext` metadata for typed serialization and
deserialization. Built-in scalar types and the Document Object Model keep working without it.

## Document Object Model

Deserialize to `TomlTable` to read a document without a model. Tables implement `IDictionary<string, object>`, arrays
implement `IList<object?>`, and scalar values are `string`, `long`, `double`, `bool`, or `TomlDateTime`:

```csharp
using Meziantou.Framework.Toml;
using Meziantou.Framework.Toml.Model;

var table = TomlSerializer.Deserialize<TomlTable>("""
    title = "My App"

    [database]
    host = "localhost"
    ports = [8000, 8001]
    """)!;

var title = (string)table["title"];
var database = (TomlTable)table["database"];
var firstPort = (long)((TomlArray)database["ports"])[0]!;
```

Build a document and serialize it:

```csharp
var document = new TomlTable
{
    ["title"] = "My App",
    ["owner"] = new TomlTable(inline: true) { ["name"] = "Ada" },
    ["database"] = new TomlTable
    {
        ["ports"] = new TomlArray { 8000L, 8001L },
    },
    ["servers"] = new TomlTableArray
    {
        new TomlTable { ["name"] = "alpha" },
        new TomlTable { ["name"] = "beta" },
    },
};

var toml = TomlSerializer.Serialize(document);
```

`TomlDateTime` keeps the kind of TOML date and time (`OffsetDateTimeByZ`, `OffsetDateTimeByNumber`, `LocalDateTime`,
`LocalDate`, or `LocalTime`) and the precision of the fractional seconds.

### Metadata and trivia

A metadata store captures comments, source locations, and how each value was written, without changing the model types:

```csharp
var store = new TomlMetadataStore();
var options = new TomlSerializerOptions { MetadataStore = store };
var model = TomlSerializer.Deserialize<TomlTable>(toml, options)!;

if (store.TryGetProperties(model, out var metadata) && metadata.TryGetProperty("title", out var property))
{
    // property.LeadingTrivia, property.TrailingTrivia, property.Span, property.DisplayKind
}
```

## Syntax tree

`SyntaxParser` builds a lossless syntax tree that keeps every character of the input, including comments and whitespace.
`ToString()` returns the original text, which makes it suitable for formatters, linters, and editors. Each token and
trivia is an object with its own position, so the tree uses about 70 times the size of the input in memory. To read
large or untrusted input, prefer `TomlSerializer` or `TomlParser`:

```csharp
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Syntax;

var document = SyntaxParser.Parse("# config\nname = \"Ada\"\n", sourceName: "config.toml");
if (document.HasErrors)
{
    foreach (var diagnostic in document.Diagnostics)
        Console.WriteLine(diagnostic);
}

Console.WriteLine(document.ToString()); // Same text as the input

foreach (var node in document.Descendants())
{
    if (node is StringValueSyntax value)
        Console.WriteLine(value.Value);
}
```

`SyntaxParser.Parse` collects errors in `Diagnostics`, including when `MaxDepth` is exceeded, while
`SyntaxParser.ParseStrict` throws a `TomlException`. The tree of an invalid document still keeps every character: the
tokens the parser skips are kept as trivia, and an `InvalidSyntaxToken` keeps the text found in place of the expected
token. A byte order mark is not a token either; `DocumentSyntax.HasByteOrderMark` records it. A `DocumentSyntax`
contains the root `KeyValues` and the `Tables`. Every node has `LeadingTrivia` and `TrailingTrivia`. Derive from
`SyntaxVisitor` to walk the tree, and use `Tokens()` to enumerate the tokens.

## Lexer and parser

`TomlLexer` produces tokens, and `TomlParser` produces parse events without building a tree. Both avoid allocations
where possible. `TomlParser` emits the events in document order, so a table defined in several places produces
several `PropertyName`/`StartTable` fragments with the same name:

```csharp
using Meziantou.Framework.Toml.Parsing;

var lexer = TomlLexer.Create("name = \"Ada\"", sourceName: "config.toml");
while (lexer.MoveNext())
{
    Console.WriteLine($"{lexer.Current.Kind} {lexer.CurrentSpan}");
}

var parser = TomlParser.Create("[server]\nport = 8080", new TomlParserOptions { Mode = TomlParserMode.Tolerant });
while (parser.MoveNext())
{
    var kind = parser.Current.Kind;
    Console.WriteLine(kind == TomlParseEventKind.PropertyName ? $"{kind} {parser.GetPropertyName()}" : kind.ToString());
}
```

`TomlParserOptions` controls the error mode (`Strict` throws on the first error, `Tolerant` collects diagnostics),
whether escape sequences are decoded, whether trivia is reported, and whether string values are materialized eagerly.

## Error handling

Parsing and mapping errors throw `TomlException`. It exposes the location of the first error (`SourceName`, `Line` and
`Column` 1-based, `Offset` 0-based, and `Span`) and every diagnostic in `Diagnostics`. Deserialization reports the
mapping errors of the whole document: a value that cannot be converted, an invalid collection element or a missing
required key does not stop the reading. Set
`TomlSerializerOptions.SourceName` to include a file name in the messages. Use `TryDeserialize` when invalid input is
expected, for example user-provided configuration files. It returns `false` for invalid input and for a `null` result,
but still throws for errors of the program, such as a type without metadata in the context.
