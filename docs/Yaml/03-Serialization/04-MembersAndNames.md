---
title: Members and names
description: Choose the YAML keys of your members, the members that are written and read, their order, and the values of enums.
---

## Naming policies

By default, a key is the name of the property, such as `DisplayName`. Set [PropertyNamingPolicy](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.PropertyNamingPolicy) to convert every name:

| Policy | `DisplayName` becomes |
| --- | --- |
| [YamlNamingPolicy.CamelCase](cref:Meziantou.Framework.Yaml.YamlNamingPolicy.CamelCase) | `displayName` |
| [YamlNamingPolicy.PascalCase](cref:Meziantou.Framework.Yaml.YamlNamingPolicy.PascalCase) | `DisplayName` |
| [YamlNamingPolicy.SnakeCaseLower](cref:Meziantou.Framework.Yaml.YamlNamingPolicy.SnakeCaseLower) | `display_name` |
| [YamlNamingPolicy.SnakeCaseUpper](cref:Meziantou.Framework.Yaml.YamlNamingPolicy.SnakeCaseUpper) | `DISPLAY_NAME` |
| [YamlNamingPolicy.KebabCaseLower](cref:Meziantou.Framework.Yaml.YamlNamingPolicy.KebabCaseLower) | `display-name` |
| [YamlNamingPolicy.KebabCaseUpper](cref:Meziantou.Framework.Yaml.YamlNamingPolicy.KebabCaseUpper) | `DISPLAY-NAME` |

For another convention, derive a class from [YamlNamingPolicy](cref:Meziantou.Framework.Yaml.YamlNamingPolicy) and override [ConvertName](cref:Meziantou.Framework.Yaml.YamlNamingPolicy.ConvertName). [DictionaryKeyPolicy](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.DictionaryKeyPolicy)
converts the keys of dictionaries when writing; by default they are written unchanged.

[\[YamlNamingPolicy\]](cref:Meziantou.Framework.Yaml.Serialization.YamlNamingPolicyAttribute) overrides the policy of the options for all the members of a type, or for one member:

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

[\[YamlPropertyName\]](cref:Meziantou.Framework.Yaml.Serialization.YamlPropertyNameAttribute) sets the key of one member. It wins over every naming policy:

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

[DefaultIgnoreCondition](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.DefaultIgnoreCondition) sets which members are skipped, and [\[YamlIgnore\]](cref:Meziantou.Framework.Yaml.Serialization.YamlIgnoreAttribute) overrides it for a type or a member:

| Condition | The member is |
| --- | --- |
| [Never](cref:Meziantou.Framework.Yaml.YamlIgnoreCondition.Never) (default) | always written and read |
| [WhenWritingNull](cref:Meziantou.Framework.Yaml.YamlIgnoreCondition.WhenWritingNull) | not written when its value is `null` |
| [WhenWritingDefault](cref:Meziantou.Framework.Yaml.YamlIgnoreCondition.WhenWritingDefault) | not written when its value is the default of its type (`null`, `0`, `false`, …) |
| [WhenWriting](cref:Meziantou.Framework.Yaml.YamlIgnoreCondition.WhenWriting) | never written, but read |
| [WhenReading](cref:Meziantou.Framework.Yaml.YamlIgnoreCondition.WhenReading) | written, but never read |
| [Always](cref:Meziantou.Framework.Yaml.YamlIgnoreCondition.Always) | never written nor read |

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

[\[YamlIgnore\]](cref:Meziantou.Framework.Yaml.Serialization.YamlIgnoreAttribute) without a condition means [Always](cref:Meziantou.Framework.Yaml.YamlIgnoreCondition.Always). [IgnoreReadOnlyProperties](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.IgnoreReadOnlyProperties) and [IgnoreReadOnlyFields](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.IgnoreReadOnlyFields) skip the
members that have no setter when writing.

## Fields and non-public members

Only the public properties are serialized by default. Set [IncludeFields](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.IncludeFields) to also serialize the public fields. Mark a
non-public property or field with [\[YamlInclude\]](cref:Meziantou.Framework.Yaml.Serialization.YamlIncludeAttribute) to serialize it.

## Order the members

Members are written in declaration order, from the base type to the derived type. [\[YamlPropertyOrder\]](cref:Meziantou.Framework.Yaml.Serialization.YamlPropertyOrderAttribute) changes the
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

An enum value is written with the name of its member. [\[YamlEnumMemberName\]](cref:Meziantou.Framework.Yaml.Serialization.YamlEnumMemberNameAttribute) sets another name:

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
