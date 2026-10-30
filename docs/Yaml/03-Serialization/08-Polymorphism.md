---
title: Polymorphism
description: Serialize a class hierarchy with a discriminator key or a YAML tag, register derived types, and choose the type from the content.
---

A member declared with a base type can hold any derived type. To write the derived type and read it back, the YAML
must say which type it is: a discriminator key, such as `$type: dog`, or a YAML tag, such as `!dog`.

## Declare the derived types

Mark the base type with `[YamlPolymorphic]` and register each derived type with `[YamlDerivedType]` and its
discriminator:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlPolymorphicAttribute_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
[YamlPolymorphic]
[YamlDerivedType(typeof(Dog), "dog")]
[YamlDerivedType(typeof(Cat), "cat")]
internal abstract class Animal
{
    public string Name { get; set; } = "";
}

internal sealed class Dog : Animal;

internal sealed class Cat : Animal;
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlPolymorphism_RoundTrip;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = YamlSerializer.Serialize<Animal>(new Dog { Name = "Rex" });
Assert.Equal("$type: dog\nName: Rex\n", yaml);

var animal = YamlSerializer.Deserialize<Animal>(yaml);
Assert.Equal("Rex", Assert.IsType<Dog>(animal).Name);
```
<!-- endref -->

The discriminator is written first. When reading, it selects the type to create, and is not passed to that type: it is
neither an unknown key nor [extension data](06-ReadingBehavior.md#extra-keys). Discriminators are strings or integers.
It is read from the mapping itself only, not from a mapping merged into it with `<<`.

A derived type registered without a discriminator nor a tag, `[YamlDerivedType(typeof(Dog))]`, is the default: it is
created when no discriminator or tag matches.

When writing, the runtime type must be registered exactly: a subclass of `Dog` that is not registered throws a
`NotSupportedException`, instead of being written as a `Dog` without its own members.

A type cannot register the same derived type, discriminator or tag twice: the serializer throws an
`InvalidOperationException` the first time the type is used.

## Options

| Setting | Values | Description |
| --- | --- | --- |
| `DiscriminatorStyle` | `Property` (default), `Tag`, `Both` | Whether the type is identified by a key, by a YAML tag, or both. |
| `TypeDiscriminatorPropertyName` | `$type` by default | The key used by the `Property` style. |
| `UnknownDerivedTypeHandling` | `Fail` (default), `FallBackToBase` | What happens when reading a discriminator that matches no derived type. |
| `InferClosedTypePolymorphism` | `false` by default | See [Closed hierarchies](#closed-hierarchies). |

Set them on `[YamlPolymorphic]` for one base type, or on `YamlSerializerOptions.PolymorphismOptions` for every type.

## Identify the type with a tag

With the `Tag` style, the type is a YAML tag on the mapping:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlTagOnlyDerivedTypeTests.cs;region=YamlTypeDiscriminatorStyle_Tag;format=md-fenced-code-block;dedent=true -->
```csharp
[YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag)]
[YamlDerivedType(typeof(Dog), Tag = "!dog")]
internal abstract class Animal
{
    public string Name { get; set; } = "";
}

internal sealed class Dog : Animal;
```
<!-- endref -->

A `Dog` named Rex is then written as `!dog` followed by `Name: Rex`.

## Register derived types from another assembly

When you cannot change the base type, for instance in a plugin system, register the derived types in the options with
`PolymorphismOptions.DerivedTypeMappings`:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlRuntimeDerivedTypeTests.cs;region=YamlPolymorphismOptions_DerivedTypeMappings;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

These registrations are merged with the attributes of the base type; the attributes win on conflicts. Assigning the
`YamlPolymorphismOptions` to the serializer options copies the mappings and makes the instance read-only: changing them
afterwards throws a `NotSupportedException`.

With a [generated context](../04-SourceGeneration.md), declare these registrations with `[YamlDerivedTypeMapping]` on
the context class instead: it is the form that the source generator understands.

## Generic base types

`[YamlDerivedType]` accepts an open generic derived type on a generic base type. It is closed for each use of the
base type:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlPolymorphismTests.cs;region=YamlDerivedTypeAttribute_OpenGeneric;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

## Closed hierarchies

A `closed` class lists its derived types itself, so they do not need `[YamlDerivedType]`:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlTagOnlyDerivedTypeTests.cs;region=YamlPolymorphism_ClosedHierarchy;format=md-fenced-code-block;dedent=true -->
```csharp
public closed class Shape
{
    public string Name { get; set; } = "";
}

public sealed class Circle : Shape { public double Radius { get; set; } }
public sealed class Square : Shape { public double Side { get; set; } }
```
<!-- endref -->

Set `InferClosedTypePolymorphism` to register every derived type of a closed base type, with the name of the type as
its discriminator:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlTagOnlyDerivedTypeTests.cs;region=YamlPolymorphismOptions_InferClosedTypePolymorphism;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new YamlSerializerOptions
{
    PolymorphismOptions = new YamlPolymorphismOptions { InferClosedTypePolymorphism = true },
};

Shape shape = new Circle { Name = "circle", Radius = 3 };
var yaml = YamlSerializer.Serialize(shape, options);
Assert.Equal("$type: Circle\nName: circle\nRadius: 3.0\n", yaml);
```
<!-- endref -->

The inference is opt-in. To enable it for one type, set it on the attribute, which wins over the options, so
`InferClosedTypePolymorphism = false` also excludes a type from a global opt-in:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs;region=YamlPolymorphicAttribute_InferClosedTypePolymorphism;format=md-fenced-code-block;dedent=true -->
```csharp
[YamlPolymorphic(InferClosedTypePolymorphism = true)]
public closed class Shape
{
}
```
<!-- endref -->

- A type with `[YamlDerivedType]` or runtime mappings registers those types only: nothing is inferred.
- A derived type that is itself `closed` brings its own derived types, so the most derived type of a fully closed
  hierarchy is the one written and read. A derived type that is not `closed` ends the inference.
- Enabling the inference on a type that is not `closed` throws an `InvalidOperationException`.
- A derived type must be at least as visible as its base type, and two derived types cannot have the same name;
  otherwise the serializer throws an `InvalidOperationException`.

## Choose the type from the content

When the YAML has no discriminator, a type classifier can choose the derived type from the content, such as the keys
of the mapping. Derive from `YamlTypeClassifierFactory`, and add it to `YamlSerializerOptions.TypeClassifiers`:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlPolymorphicClassifierTests.cs;region=YamlTypeClassifierFactory_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
internal sealed class ShapeClassifier : YamlTypeClassifierFactory
{
    public override bool CanClassify(YamlTypeClassifierContext context)
        => context.Kind is YamlTypeClassifierKind.PolymorphicType && context.DeclaringType == typeof(Shape);

    public override YamlTypeClassifier CreateYamlClassifier(YamlTypeClassifierContext context, YamlSerializerOptions options)
        => reader =>
        {
            // The classifier reads a copy of the value: select the derived type from the first key of the mapping
            if (reader.TokenType is not YamlTokenType.StartMapping)
                return null;

            reader.Read();
            var type = reader.GetScalarValue() switch
            {
                "Radius" => typeof(Circle),
                "Side" => typeof(Square),
                _ => null,
            };

            return context.DerivedTypes.Any(derivedType => derivedType.DerivedType == type) ? type : null;
        };
}
```
<!-- endref -->

- `CanClassify` receives a context whose `Kind` is `YamlTypeClassifierKind.PolymorphicType` for a base type, or
  `Union` for a [C# union](09-Unions.md).
- The classifier reads a copy of the value, so it can move the reader freely.
- It runs only when no discriminator or tag selected the type. Returning `null` falls back to the default derived type,
  or to the usual error.

`YamlTypeClassification.Classify` and `ClassifyBufferedNode` expose the buffering that classifiers use, to inspect a
node and let the serializer read it afterwards.
