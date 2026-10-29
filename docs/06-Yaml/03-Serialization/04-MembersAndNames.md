---
title: Members and names
description: Choose the YAML keys of your members, the members that are written and read, their order, and the values of enums.
---

## Naming policies

By default, a key is the name of the property, such as `DisplayName`. Set `PropertyNamingPolicy` to convert every name:

| Policy | `DisplayName` becomes |
| --- | --- |
| `YamlNamingPolicy.CamelCase` | `displayName` |
| `YamlNamingPolicy.PascalCase` | `DisplayName` |
| `YamlNamingPolicy.SnakeCaseLower` | `display_name` |
| `YamlNamingPolicy.SnakeCaseUpper` | `DISPLAY_NAME` |
| `YamlNamingPolicy.KebabCaseLower` | `display-name` |
| `YamlNamingPolicy.KebabCaseUpper` | `DISPLAY-NAME` |

For another convention, derive a class from `YamlNamingPolicy` and override `ConvertName`. `DictionaryKeyPolicy`
converts the keys of dictionaries when writing; by default they are written unchanged.

`[YamlNamingPolicy]` overrides the policy of the options for all the members of a type, or for one member:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlNamingPolicyAttributeTests.cs;region=YamlNamingPolicyAttribute_Usage;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

## Rename a member

`[YamlPropertyName]` sets the key of one member. It wins over every naming policy:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerSourceGenerationTests.cs;region=YamlSerializer_Product;format=md-fenced-code-block;dedent=true -->
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

Two members that end up with the same key throw an `InvalidOperationException` the first time the type is used.

## Ignore members

`DefaultIgnoreCondition` sets which members are skipped, and `[YamlIgnore]` overrides it for a type or a member:

| Condition | The member is |
| --- | --- |
| `Never` (default) | always written and read |
| `WhenWritingNull` | not written when its value is `null` |
| `WhenWritingDefault` | not written when its value is the default of its type (`null`, `0`, `false`, …) |
| `WhenWriting` | never written, but read |
| `WhenReading` | written, but never read |
| `Always` | never written nor read |

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlIgnoreAttributeTests.cs;region=YamlIgnoreAttribute_Usage;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

`[YamlIgnore]` without a condition means `Always`. `IgnoreReadOnlyProperties` and `IgnoreReadOnlyFields` skip the
members that have no setter when writing.

## Fields and non-public members

Only the public properties are serialized by default. Set `IncludeFields` to also serialize the public fields. Mark a
non-public property or field with `[YamlInclude]` to serialize it.

## Order the members

Members are written in declaration order, from the base type to the derived type. `[YamlPropertyOrder]` changes the
position of a member: lower values come first, and a member without the attribute has the order `0`.

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlObjectConverterContractTests.cs;region=YamlPropertyOrderAttribute_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class Product
{
    [YamlPropertyOrder(1)]
    public int Id { get; set; }

    [YamlPropertyOrder(2)]
    public string? Name { get; set; }
}
```
<!-- endref -->

To sort the keys by name instead, see [Key order](05-OutputFormat.md#key-order).

## Enum values

An enum value is written with the name of its member. `[YamlEnumMemberName]` sets another name:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlEnumMemberNameTests.cs;region=YamlEnumMemberNameAttribute_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
internal enum Level
{
    Low,

    [YamlEnumMemberName("very-high")]
    VeryHigh,                                         // very-high
}
```
<!-- endref -->

When reading, the name is matched ignoring the case, and a number is accepted as the numeric value of the member.
