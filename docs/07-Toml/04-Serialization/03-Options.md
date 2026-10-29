---
title: Options
description: Configure the serializer with TomlSerializerOptions, naming policies, what is written, strict reading, and the layout of the output.
---

Pass a `TomlSerializerOptions` to `Serialize`, `Deserialize` and `TryDeserialize`:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSerializerOptions_Create;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new TomlSerializerOptions
{
    PropertyNamingPolicy = TomlNamingPolicy.SnakeCaseLower,
    PreferredObjectCreationHandling = TomlObjectCreationHandling.Replace,
    WriteIndented = true,
    IndentSize = 4,
    MaxDepth = 64,
    DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingNull,
};

var config = new ServerConfig { Host = "example.com" };
var toml = TomlSerializer.Serialize(config, options);
Assert.Equal("""
    host = "example.com"
    port = 8080

    """, toml);
```
<!-- endref -->

`TomlSerializerOptions` is an immutable record: use a `with` expression to derive an instance from another one. It
caches the metadata of your types on first use, so create an instance once, for example in a `static readonly` field,
and reuse it.

With the source generator, set the options with `[TomlSourceGenerationOptions]` on the context instead: see
[Source generation](../05-SourceGeneration.md#set-the-options).

## Name the keys

| Option | Default | Effect |
| --- | --- | --- |
| `PropertyNamingPolicy` | `null` | Converts member names to keys. `null` keeps the C# names, like `System.Text.Json`. |
| `DictionaryKeyPolicy` | `null` | Converts dictionary keys when they are written. |
| `PropertyNameCaseInsensitive` | `false` | Matches keys to members ignoring case when reading. |

The naming policies are `TomlNamingPolicy.CamelCase`, `PascalCase`, `SnakeCaseLower`, `SnakeCaseUpper`,
`KebabCaseLower` and `KebabCaseUpper`. To write your own, derive from `TomlNamingPolicy`. `[TomlPropertyName]` sets
the key of one member and wins over the policy.

## Choose what is written

| Option | Default | Effect |
| --- | --- | --- |
| `DefaultIgnoreCondition` | `WhenWritingNull` | Skips `null` values (`WhenWritingNull`), also default values (`WhenWritingDefault`), or nothing (`Never`). |
| `IncludeFields` | `false` | Includes public fields. |
| `IgnoreReadOnlyProperties` | `false` | Skips properties without a public setter when writing. An `init` property is not read-only. |
| `IgnoreReadOnlyFields` | `false` | Skips `readonly` fields when writing. |

TOML has no null: a `null` member that is not skipped throws a `TomlException`. `[TomlIgnore(Condition = ...)]`
overrides `DefaultIgnoreCondition` for one member, including with `Never`.

## Choose how strict reading is

| Option | Default | Effect |
| --- | --- | --- |
| `UnmappedMemberHandling` | `Skip` | Skips keys that match no member, or rejects them (`Disallow`). Extension data still collects them. |
| `DuplicateKeyHandling` | `Error` | Rejects a key assigned twice, or keeps the last value (`LastWins`). A table defined twice is always rejected. |
| `RespectRequiredConstructorParameters` | `true` | A constructor parameter without a default value must be present. When `false`, it receives the default value of its type. |
| `RespectNullableAnnotations` | `true` | Rejects `null` in members and constructor parameters declared as non-nullable reference types, when reading and writing. |
| `PreferredObjectCreationHandling` | `Replace` | Replaces object and collection members, or fills the existing instances (`Populate`). See [Mapping members](04-MappingMembers.md#fill-existing-values). |
| `MaxInputLength` | `0` (no limit) | Maximum length of the input: characters for a string or a `TextReader`, bytes for a `Stream`. |
| `MaxDepth` | `0` (64) | Maximum nesting depth of tables and arrays. |

`RespectNullableAnnotations` applies before `DefaultIgnoreCondition`: a `null` in a non-nullable member throws instead of
being skipped, unless the member itself has `[TomlIgnore(Condition = TomlIgnoreCondition.WhenWritingNull)]` or
`WhenWritingDefault`.

For the limits to set for files you don't trust, see
[Handling errors](07-ErrorHandling.md#read-untrusted-input).

## Layout of the output

| Option | Default | Effect |
| --- | --- | --- |
| `WriteIndented` | `false` | Indents the header and the keys of a table once for each table it is nested in. |
| `IndentSize` | `2` | Number of spaces per level. |
| `NewLine` | `Lf` | Line endings: `Lf` or `CrLf`. |
| `MappingOrder` | `OrderThenDeclaration` | Order of the members: `Declaration`, `Alphabetical`, `OrderThenDeclaration` or `OrderThenAlphabetical`. The `Order...` values honor `[TomlPropertyOrder]`. |
| `InlineTablePolicy` | `Never` | Writes nested objects as inline tables (`Owner = {Name = "Ada"}`): `Never`, `WhenSmall` or `Always`. |
| `TableArrayStyle` | `Headers` | Writes arrays of tables with `[[name]]` headers, or as inline arrays of inline tables (`InlineArrayOfTables`). |
| `DottedKeyHandling` | `Literal` | Writes a member name or a dictionary key that contains a dot as a quoted key (`Literal`), or expands it into nested tables (`Expand`). |

Declaration order lists the members of the base types first, then, in each type, the fields and then the properties.

`DottedKeyHandling` does not change reading: with `Expand`, the keys are read back as nested tables, so such a model does
not round-trip. The keys of a `TomlTable` are always written as they are.

### String styles

`StringStylePreferences` sets how strings are written:

- `DefaultStyle`: `Basic` (`"text"`, the default), `Literal` (`'text'`), `MultilineBasic` or `MultilineLiteral`.
- `PreferLiteralWhenNoEscapes`: writes a literal string when the value needs no escape.
- `AllowHexEscapes`: escapes control characters with the TOML 1.1 `\e` and `\xHH` instead of `\uXXXX`. TOML 1.0
  readers reject them; see [TOML 1.0 and 1.1](../03-TomlVersions.md#write-the-toml-11-escapes).

### Formatting attributes

Attributes set the layout of one type or member. They only change how values are written:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlFormattingAttributes_Usage;format=md-fenced-code-block;dedent=true -->
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

public sealed class Owner
{
    public string Name { get; set; } = "";
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlFormattingAttributes_Write;format=md-fenced-code-block;dedent=true -->
```csharp
var toml = TomlSerializer.Serialize(new FormattedConfig
{
    Package = [new Package { Name = "core" }],
    Owner = new Owner { Name = "Ada" },
});

Assert.Equal("""
    Description = 'plain text'
    Owner = {Name = "Ada"}
    [[Package]]
    Name = "core"

    """, toml);
```
<!-- endref -->

| Attribute | Applies to | Overrides |
| --- | --- | --- |
| `[TomlTableArrayStyle]` | A collection member | `TableArrayStyle` |
| `[TomlInlineTable]` | A member | `InlineTablePolicy`, for this value only |
| `[TomlStringStyle]` | A `string` member | `StringStylePreferences` |
| `[TomlMappingOrder]` | A class or a struct | `MappingOrder` |
| `[TomlDottedKeyHandling]` | A class or a struct | `DottedKeyHandling`, for the member names of the type |

## Other options

| Option | Default | Effect |
| --- | --- | --- |
| `RootValueHandling` | `Error` | When the root value is not a table, throws or writes it under `RootValueKeyName` (`WrapInRootKey`). |
| `RootValueKeyName` | `"value"` | The key used by `WrapInRootKey`. |
| `PolymorphismOptions` | `$type` discriminator | The discriminator key, the handling of unknown types, and derived types registered outside the base type. See [Polymorphism](06-Polymorphism.md). |
| `Converters` | Empty | Custom converters. They win over the built-in converters. See [Custom converters](05-Converters.md). |
| `TypeInfoResolver` | `null` | The source of the metadata, for example a source-generated context. |
| `MetadataStore` | `null` | Keeps the comments and the source locations of what is read. See [Document model](../07-DocumentModel.md#comments-and-source-locations). |
| `SourceName` | `null` | The file name shown in the `TomlException` messages. |

`options.GetTypeInfo<T>()` returns the metadata the options resolve for a type, from the converters, the
`TypeInfoResolver`, the built-in types or reflection. `TryGetTypeInfo<T>()` returns `false` when there is none.
