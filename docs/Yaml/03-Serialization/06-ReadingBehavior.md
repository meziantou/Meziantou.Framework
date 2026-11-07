---
title: Reading behavior
description: Decide what happens with unknown, missing and duplicate keys, fill existing collections, keep extra keys, and run code around serialization.
---

## Unknown keys

A key that matches no member is skipped. To reject it, which catches typos in configuration files, set
[UnmappedMemberHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.UnmappedMemberHandling) to [Disallow](cref:Meziantou.Framework.Yaml.YamlUnmappedMemberHandling.Disallow):

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlSerializerOptions_With;format=md-fenced-code-block;dedent=true -->
```csharp
var strict = YamlSerializerOptions.Default with
{
    UnmappedMemberHandling = YamlUnmappedMemberHandling.Disallow,
};

Assert.Equal(1, YamlSerializer.Deserialize<Product>("Id: 1\nPrice: 10")!.Id); // Price is ignored
Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Product>("Id: 1\nPrice: 10", strict));
```
<!-- endref -->

[\[YamlUnmappedMemberHandling\]](cref:Meziantou.Framework.Yaml.Serialization.YamlUnmappedMemberHandlingAttribute) sets the behavior for one type, whatever the options say. To keep the unknown keys
instead, see [Extra keys](#extra-keys).

Keys are matched with the names of the members exactly. Set [PropertyNameCaseInsensitive](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.PropertyNameCaseInsensitive) to ignore the case.

## Missing and null values

A missing key leaves the member with the value set by the constructor, except for:

- the parameters without a default value of the constructor used to create the object;
- the properties declared with the `required` keyword;
- the members marked with [\[YamlRequired\]](cref:Meziantou.Framework.Yaml.Serialization.YamlRequiredAttribute).

These throw a [YamlException](cref:Meziantou.Framework.Yaml.YamlException) when their key is missing. See [Objects](03-SupportedTypes.md#objects) for the
constructors and for how `null` values are handled.

## Duplicate keys

A mapping that contains the same key twice throws a [YamlException](cref:Meziantou.Framework.Yaml.YamlException). Set [DuplicateKeyHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.DuplicateKeyHandling) to
[YamlDuplicateKeyHandling.LastWins](cref:Meziantou.Framework.Yaml.YamlDuplicateKeyHandling.LastWins) or [FirstWins](cref:Meziantou.Framework.Yaml.YamlDuplicateKeyHandling.FirstWins) to accept it and keep one of the values.

## Numbers in strings

A quoted number, such as `"8080"`, is read into a numeric member like a plain one. [\[YamlNumberHandling\]](cref:Meziantou.Framework.Yaml.Serialization.YamlNumberHandlingAttribute) changes how
numbers are handled for a member or a type:

- [AllowNamedFloatingPointLiterals](cref:Meziantou.Framework.Yaml.YamlNumberHandling.AllowNamedFloatingPointLiterals) reads and writes the special values of the floating-point types as the quoted
  `"NaN"`, `"Infinity"` and `"-Infinity"`, instead of the YAML `.nan`, `.inf` and `-.inf`.
- [AllowReadingFromString](cref:Meziantou.Framework.Yaml.YamlNumberHandling.AllowReadingFromString) also lets a quoted number select a numeric case of a [C# union](09-Unions.md#numbers-in-strings).
- [WriteAsString](cref:Meziantou.Framework.Yaml.YamlNumberHandling.WriteAsString) writes the numbers quoted: see [Numbers as strings](05-OutputFormat.md#numbers-as-strings).

## Fill existing collections

By default, a member is replaced by a new value. With [YamlObjectCreationHandling.Populate](cref:Meziantou.Framework.Yaml.YamlObjectCreationHandling.Populate), the serializer adds the
items to the collection that the member already holds, or sets the members of the object that it already holds:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlObjectCreationHandlingTests.cs;region=YamlObjectCreationHandling_Settings;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class Settings
{
    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    public List<string> Tags { get; set; } = ["default"];
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlObjectCreationHandlingTests.cs;region=YamlObjectCreationHandling_Deserialize;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = YamlSerializer.Deserialize<Settings>("Tags:\n  - extra\n");
Assert.Equal(["default", "extra"], settings!.Tags);
```
<!-- endref -->

[\[YamlObjectCreationHandling\]](cref:Meziantou.Framework.Yaml.Serialization.YamlObjectCreationHandlingAttribute) applies to a member or a type; [PreferredObjectCreationHandling](cref:Meziantou.Framework.Yaml.YamlSerializerOptions.PreferredObjectCreationHandling) applies to every member.

## Extra keys

[\[YamlExtensionData\]](cref:Meziantou.Framework.Yaml.Serialization.YamlExtensionDataAttribute) collects the keys that match no member, and writes them back when serializing:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlExtensionDataTests.cs;region=YamlExtensionDataAttribute_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class Product
{
    public int Id { get; set; }

    [YamlExtensionData]
    public IReadOnlyDictionary<string, object?>? Extra { get; set; }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlExtensionDataTests.cs;region=YamlExtensionDataAttribute_Deserialize;format=md-fenced-code-block;dedent=true -->
```csharp
var product = YamlSerializer.Deserialize<Product>("Id: 1\nColor: red\n");

Assert.Equal(1, product!.Id);
Assert.Equal("red", product.Extra!["Color"]);
Assert.Equal("Id: 1\nColor: red\n", YamlSerializer.Serialize(product)); // written back
```
<!-- endref -->

The member must be a [YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping), or a `Dictionary<string, TValue>`, `IDictionary<string, TValue>` or
`IReadOnlyDictionary<string, TValue>` whose values are `object` or [YamlNode](cref:Meziantou.Framework.Yaml.Model.YamlNode). With [YamlNode](cref:Meziantou.Framework.Yaml.Model.YamlNode) values, or a
[YamlMapping](cref:Meziantou.Framework.Yaml.Model.YamlMapping), the extra content keeps its original form instead of being converted to .NET values. A type can have one
extension data member only.

For a read-only dictionary, the serializer creates a `Dictionary<string, TValue>` with the current entries and the new
ones, and assigns it, so the member must have a setter.

## Run code around serialization

Implement [IYamlOnSerializing](cref:Meziantou.Framework.Yaml.Serialization.IYamlOnSerializing), [IYamlOnSerialized](cref:Meziantou.Framework.Yaml.Serialization.IYamlOnSerialized), [IYamlOnDeserializing](cref:Meziantou.Framework.Yaml.Serialization.IYamlOnDeserializing) or [IYamlOnDeserialized](cref:Meziantou.Framework.Yaml.Serialization.IYamlOnDeserialized) to run code
before or after an object is written or read, for instance to validate it after reading:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlLifecycleCallbackTests.cs;region=IYamlOnSerializing_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class Product : IYamlOnSerializing, IYamlOnSerialized, IYamlOnDeserializing, IYamlOnDeserialized
{
    public void OnSerializing() { }
    public void OnSerialized() { }
    public void OnDeserializing() { }
    public void OnDeserialized() { }
}
```
<!-- endref -->

An exception thrown by a callback is wrapped in a [YamlException](cref:Meziantou.Framework.Yaml.YamlException).
