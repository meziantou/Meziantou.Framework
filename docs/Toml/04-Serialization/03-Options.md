---
title: Options
description: Configure the serializer with TomlSerializerOptions, naming policies, what is written, strict reading, and the layout of the output.
---

Pass a [TomlSerializerOptions](cref:Meziantou.Framework.Toml.TomlSerializerOptions) to [Serialize](cref:Meziantou.Framework.Toml.TomlSerializer.Serialize), [Deserialize](cref:Meziantou.Framework.Toml.TomlSerializer.Deserialize) and [TryDeserialize](cref:Meziantou.Framework.Toml.TomlSerializer.TryDeserialize):

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

[TomlSerializerOptions](cref:Meziantou.Framework.Toml.TomlSerializerOptions) is an immutable record: use a `with` expression to derive an instance from another one. It
caches the metadata of your types on first use, so create an instance once, for example in a `static readonly` field,
and reuse it.

With the source generator, set the options with [\[TomlSourceGenerationOptions\]](cref:Meziantou.Framework.Toml.Serialization.TomlSourceGenerationOptionsAttribute) on the context instead: see
[Source generation](../05-SourceGeneration.md#set-the-options).

## Name the keys

| Option | Default | Effect |
| --- | --- | --- |
| [PropertyNamingPolicy](cref:Meziantou.Framework.Toml.TomlSerializerOptions.PropertyNamingPolicy) | `null` | Converts member names to keys. `null` keeps the C# names, like `System.Text.Json`. |
| [DictionaryKeyPolicy](cref:Meziantou.Framework.Toml.TomlSerializerOptions.DictionaryKeyPolicy) | `null` | Converts dictionary keys when they are written. |
| [PropertyNameCaseInsensitive](cref:Meziantou.Framework.Toml.TomlSerializerOptions.PropertyNameCaseInsensitive) | `false` | Matches keys to members ignoring case when reading. |

The naming policies are [TomlNamingPolicy.CamelCase](cref:Meziantou.Framework.Toml.TomlNamingPolicy.CamelCase), [PascalCase](cref:Meziantou.Framework.Toml.TomlNamingPolicy.PascalCase), [SnakeCaseLower](cref:Meziantou.Framework.Toml.TomlNamingPolicy.SnakeCaseLower), [SnakeCaseUpper](cref:Meziantou.Framework.Toml.TomlNamingPolicy.SnakeCaseUpper),
[KebabCaseLower](cref:Meziantou.Framework.Toml.TomlNamingPolicy.KebabCaseLower) and [KebabCaseUpper](cref:Meziantou.Framework.Toml.TomlNamingPolicy.KebabCaseUpper). To write your own, derive from [TomlNamingPolicy](cref:Meziantou.Framework.Toml.TomlNamingPolicy). [\[TomlPropertyName\]](cref:Meziantou.Framework.Toml.Serialization.TomlPropertyNameAttribute) sets
the key of one member and wins over the policy.

## Choose what is written

| Option | Default | Effect |
| --- | --- | --- |
| [DefaultIgnoreCondition](cref:Meziantou.Framework.Toml.TomlSerializerOptions.DefaultIgnoreCondition) | [WhenWritingNull](cref:Meziantou.Framework.Toml.TomlIgnoreCondition.WhenWritingNull) | Skips `null` values ([WhenWritingNull](cref:Meziantou.Framework.Toml.TomlIgnoreCondition.WhenWritingNull)), also default values ([WhenWritingDefault](cref:Meziantou.Framework.Toml.TomlIgnoreCondition.WhenWritingDefault)), or nothing ([Never](cref:Meziantou.Framework.Toml.TomlInlineTablePolicy.Never)). |
| [IncludeFields](cref:Meziantou.Framework.Toml.TomlSerializerOptions.IncludeFields) | `false` | Includes public fields. |
| [IgnoreReadOnlyProperties](cref:Meziantou.Framework.Toml.TomlSerializerOptions.IgnoreReadOnlyProperties) | `false` | Skips properties without a public setter when writing. An `init` property is not read-only. |
| [IgnoreReadOnlyFields](cref:Meziantou.Framework.Toml.TomlSerializerOptions.IgnoreReadOnlyFields) | `false` | Skips `readonly` fields when writing. |

TOML has no null: a `null` member that is not skipped throws a [TomlException](cref:Meziantou.Framework.Toml.TomlException). `[TomlIgnore(Condition = ...)]`
overrides [DefaultIgnoreCondition](cref:Meziantou.Framework.Toml.TomlSerializerOptions.DefaultIgnoreCondition) for one member, including with [Never](cref:Meziantou.Framework.Toml.TomlInlineTablePolicy.Never).

## Choose how strict reading is

| Option | Default | Effect |
| --- | --- | --- |
| [UnmappedMemberHandling](cref:Meziantou.Framework.Toml.TomlSerializerOptions.UnmappedMemberHandling) | [Skip](cref:Meziantou.Framework.Toml.TomlUnmappedMemberHandling.Skip) | Skips keys that match no member, or rejects them ([Disallow](cref:Meziantou.Framework.Toml.TomlUnmappedMemberHandling.Disallow)). Extension data still collects them. |
| [DuplicateKeyHandling](cref:Meziantou.Framework.Toml.TomlSerializerOptions.DuplicateKeyHandling) | `Error` | Rejects a key assigned twice, or keeps the last value ([LastWins](cref:Meziantou.Framework.Toml.TomlDuplicateKeyHandling.LastWins)). A table defined twice is always rejected. |
| [RespectRequiredConstructorParameters](cref:Meziantou.Framework.Toml.TomlSerializerOptions.RespectRequiredConstructorParameters) | `true` | A constructor parameter without a default value must be present. When `false`, it receives the default value of its type. |
| [RespectNullableAnnotations](cref:Meziantou.Framework.Toml.TomlSerializerOptions.RespectNullableAnnotations) | `true` | Rejects `null` in members and constructor parameters declared as non-nullable reference types, when reading and writing. |
| [PreferredObjectCreationHandling](cref:Meziantou.Framework.Toml.TomlSerializerOptions.PreferredObjectCreationHandling) | [Replace](cref:Meziantou.Framework.Toml.TomlObjectCreationHandling.Replace) | Replaces object and collection members, or fills the existing instances ([Populate](cref:Meziantou.Framework.Toml.TomlObjectCreationHandling.Populate)). See [Mapping members](04-MappingMembers.md#fill-existing-values). |
| [MaxInputLength](cref:Meziantou.Framework.Toml.TomlSerializerOptions.MaxInputLength) | `0` (no limit) | Maximum length of the input: characters for a string or a `TextReader`, bytes for a `Stream`. |
| [MaxDepth](cref:Meziantou.Framework.Toml.TomlSerializerOptions.MaxDepth) | `0` (64) | Maximum nesting depth of tables and arrays. |

[RespectNullableAnnotations](cref:Meziantou.Framework.Toml.TomlSerializerOptions.RespectNullableAnnotations) applies before [DefaultIgnoreCondition](cref:Meziantou.Framework.Toml.TomlSerializerOptions.DefaultIgnoreCondition): a `null` in a non-nullable member throws instead of
being skipped, unless the member itself has `[TomlIgnore(Condition = TomlIgnoreCondition.WhenWritingNull)]` or
[WhenWritingDefault](cref:Meziantou.Framework.Toml.TomlIgnoreCondition.WhenWritingDefault).

For the limits to set for files you don't trust, see
[Handling errors](07-ErrorHandling.md#read-untrusted-input).

## Layout of the output

| Option | Default | Effect |
| --- | --- | --- |
| [WriteIndented](cref:Meziantou.Framework.Toml.TomlSerializerOptions.WriteIndented) | `false` | Indents the header and the keys of a table once for each table it is nested in. |
| [IndentSize](cref:Meziantou.Framework.Toml.TomlSerializerOptions.IndentSize) | `2` | Number of spaces per level. |
| `NewLine` | [Lf](cref:Meziantou.Framework.Toml.TomlNewLineKind.Lf) | Line endings: [Lf](cref:Meziantou.Framework.Toml.TomlNewLineKind.Lf) or [CrLf](cref:Meziantou.Framework.Toml.TomlNewLineKind.CrLf). |
| [MappingOrder](cref:Meziantou.Framework.Toml.TomlSerializerOptions.MappingOrder) | [OrderThenDeclaration](cref:Meziantou.Framework.Toml.TomlMappingOrderPolicy.OrderThenDeclaration) | Order of the members: [Declaration](cref:Meziantou.Framework.Toml.TomlMappingOrderPolicy.Declaration), [Alphabetical](cref:Meziantou.Framework.Toml.TomlMappingOrderPolicy.Alphabetical), [OrderThenDeclaration](cref:Meziantou.Framework.Toml.TomlMappingOrderPolicy.OrderThenDeclaration) or [OrderThenAlphabetical](cref:Meziantou.Framework.Toml.TomlMappingOrderPolicy.OrderThenAlphabetical). The `Order...` values honor [\[TomlPropertyOrder\]](cref:Meziantou.Framework.Toml.Serialization.TomlPropertyOrderAttribute). |
| [InlineTablePolicy](cref:Meziantou.Framework.Toml.TomlSerializerOptions.InlineTablePolicy) | [Never](cref:Meziantou.Framework.Toml.TomlInlineTablePolicy.Never) | Writes nested objects as inline tables (`Owner = {Name = "Ada"}`): [Never](cref:Meziantou.Framework.Toml.TomlInlineTablePolicy.Never), [WhenSmall](cref:Meziantou.Framework.Toml.TomlInlineTablePolicy.WhenSmall) or [Always](cref:Meziantou.Framework.Toml.TomlInlineTablePolicy.Always). |
| [TableArrayStyle](cref:Meziantou.Framework.Toml.TomlSerializerOptions.TableArrayStyle) | `Headers` | Writes arrays of tables with `[[name]]` headers, or as inline arrays of inline tables ([InlineArrayOfTables](cref:Meziantou.Framework.Toml.TomlTableArrayStyle.InlineArrayOfTables)). |
| [DottedKeyHandling](cref:Meziantou.Framework.Toml.TomlSerializerOptions.DottedKeyHandling) | [Literal](cref:Meziantou.Framework.Toml.TomlDottedKeyHandling.Literal) | Writes a member name or a dictionary key that contains a dot as a quoted key ([Literal](cref:Meziantou.Framework.Toml.TomlDottedKeyHandling.Literal)), or expands it into nested tables ([Expand](cref:Meziantou.Framework.Toml.TomlDottedKeyHandling.Expand)). |

Declaration order lists the members of the base types first, then, in each type, the fields and then the properties.

[DottedKeyHandling](cref:Meziantou.Framework.Toml.TomlSerializerOptions.DottedKeyHandling) does not change reading: with [Expand](cref:Meziantou.Framework.Toml.TomlDottedKeyHandling.Expand), the keys are read back as nested tables, so such a model does
not round-trip. The keys of a [TomlTable](cref:Meziantou.Framework.Toml.Model.TomlTable) are always written as they are.

### String styles

[StringStylePreferences](cref:Meziantou.Framework.Toml.TomlSerializerOptions.StringStylePreferences) sets how strings are written:

- [DefaultStyle](cref:Meziantou.Framework.Toml.TomlStringStylePreferences.DefaultStyle): [Basic](cref:Meziantou.Framework.Toml.TomlStringStyle.Basic) (`"text"`, the default), [Literal](cref:Meziantou.Framework.Toml.TomlDottedKeyHandling.Literal) (`'text'`), [MultilineBasic](cref:Meziantou.Framework.Toml.TomlStringStyle.MultilineBasic) or [MultilineLiteral](cref:Meziantou.Framework.Toml.TomlStringStyle.MultilineLiteral).
- [PreferLiteralWhenNoEscapes](cref:Meziantou.Framework.Toml.TomlStringStylePreferences.PreferLiteralWhenNoEscapes): writes a literal string when the value needs no escape.
- [AllowHexEscapes](cref:Meziantou.Framework.Toml.TomlStringStylePreferences.AllowHexEscapes): escapes control characters with the TOML 1.1 `\e` and `\xHH` instead of `\uXXXX`. TOML 1.0
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
| [\[TomlTableArrayStyle\]](cref:Meziantou.Framework.Toml.Serialization.TomlTableArrayStyleAttribute) | A collection member | [TableArrayStyle](cref:Meziantou.Framework.Toml.TomlSerializerOptions.TableArrayStyle) |
| [\[TomlInlineTable\]](cref:Meziantou.Framework.Toml.Serialization.TomlInlineTableAttribute) | A member | [InlineTablePolicy](cref:Meziantou.Framework.Toml.TomlSerializerOptions.InlineTablePolicy), for this value only |
| [\[TomlStringStyle\]](cref:Meziantou.Framework.Toml.Serialization.TomlStringStyleAttribute) | A `string` member | [StringStylePreferences](cref:Meziantou.Framework.Toml.TomlSerializerOptions.StringStylePreferences) |
| [\[TomlMappingOrder\]](cref:Meziantou.Framework.Toml.Serialization.TomlMappingOrderAttribute) | A class or a struct | [MappingOrder](cref:Meziantou.Framework.Toml.TomlSerializerOptions.MappingOrder) |
| [\[TomlDottedKeyHandling\]](cref:Meziantou.Framework.Toml.Serialization.TomlDottedKeyHandlingAttribute) | A class or a struct | [DottedKeyHandling](cref:Meziantou.Framework.Toml.TomlSerializerOptions.DottedKeyHandling), for the member names of the type |

## Other options

| Option | Default | Effect |
| --- | --- | --- |
| [RootValueHandling](cref:Meziantou.Framework.Toml.TomlSerializerOptions.RootValueHandling) | `Error` | When the root value is not a table, throws or writes it under [RootValueKeyName](cref:Meziantou.Framework.Toml.TomlSerializerOptions.RootValueKeyName) ([WrapInRootKey](cref:Meziantou.Framework.Toml.TomlRootValueHandling.WrapInRootKey)). |
| [RootValueKeyName](cref:Meziantou.Framework.Toml.TomlSerializerOptions.RootValueKeyName) | `"value"` | The key used by [WrapInRootKey](cref:Meziantou.Framework.Toml.TomlRootValueHandling.WrapInRootKey). |
| [PolymorphismOptions](cref:Meziantou.Framework.Toml.TomlSerializerOptions.PolymorphismOptions) | `$type` discriminator | The discriminator key, the handling of unknown types, and derived types registered outside the base type. See [Polymorphism](06-Polymorphism.md). |
| [Converters](cref:Meziantou.Framework.Toml.TomlSerializerOptions.Converters) | Empty | Custom converters. They win over the built-in converters. See [Custom converters](05-Converters.md). |
| [TypeInfoResolver](cref:Meziantou.Framework.Toml.TomlSerializerOptions.TypeInfoResolver) | `null` | The source of the metadata, for example a source-generated context. |
| [MetadataStore](cref:Meziantou.Framework.Toml.TomlSerializerOptions.MetadataStore) | `null` | Keeps the comments and the source locations of what is read. See [Document model](../07-DocumentModel.md#comments-and-source-locations). |
| [SourceName](cref:Meziantou.Framework.Toml.TomlSerializerOptions.SourceName) | `null` | The file name shown in the [TomlException](cref:Meziantou.Framework.Toml.TomlException) messages. |

`options.GetTypeInfo<T>()` returns the metadata the options resolve for a type, from the converters, the
[TypeInfoResolver](cref:Meziantou.Framework.Toml.TomlSerializerOptions.TypeInfoResolver), the built-in types or reflection. [TryGetTypeInfo\<T\>()](cref:Meziantou.Framework.Toml.TomlSerializerOptions.TryGetTypeInfo) returns `false` when there is none.
