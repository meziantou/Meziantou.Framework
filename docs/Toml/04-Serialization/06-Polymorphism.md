---
title: Polymorphism
description: Read and write derived types with a discriminator key, choose a default type, and register derived types from another project.
---

A member typed as a base class can hold derived types. The serializer writes a discriminator key that names the type,
and reads it to create the right type.

## Declare the derived types

Mark the base type with [\[TomlPolymorphic\]](cref:Meziantou.Framework.Toml.Serialization.TomlPolymorphicAttribute), and list the derived types with [\[TomlDerivedType\]](cref:Meziantou.Framework.Toml.Serialization.TomlDerivedTypeAttribute):

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlPolymorphism_Usage;format=md-fenced-code-block;dedent=true -->
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
<!-- endref -->

The discriminator key is `$type` by default; [TypeDiscriminatorPropertyName](cref:Meziantou.Framework.Toml.TomlPolymorphismOptions.TypeDiscriminatorPropertyName) renames it, here to `kind`. Each table
says which type it holds:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlPolymorphism_Read;format=md-fenced-code-block;dedent=true -->
```csharp
var shelter = TomlSerializer.Deserialize<Dictionary<string, List<Animal>>>("""
    [[animals]]
    kind = "cat"
    Name = "Whiskers"
    Indoor = true

    [[animals]]
    kind = "dog"
    Name = "Rex"
    Breed = "Labrador"
    """)!;

var animals = shelter["animals"];
Assert.True(Assert.IsType<Cat>(animals[0]).Indoor);
Assert.Equal("Labrador", Assert.IsType<Dog>(animals[1]).Breed);
Assert.Equal("Rex", animals[1].Name);
```
<!-- endref -->

- An integer discriminator (`[TomlDerivedType(typeof(Circle), 1)]`) is written as a string.
- To change the discriminator key of every type, set [TomlPolymorphismOptions.TypeDiscriminatorPropertyName](cref:Meziantou.Framework.Toml.TomlPolymorphismOptions.TypeDiscriminatorPropertyName) in the
  [PolymorphismOptions](cref:Meziantou.Framework.Toml.TomlSerializerOptions.PolymorphismOptions) of the options.

## Handle unknown types

- A derived type registered without a discriminator is the default type: it is used when the discriminator is missing
  or unknown, and it is written without a discriminator. `System.Text.Json` throws on an unknown discriminator instead.
- Without a default type, an unknown discriminator throws. Set
  `UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.FallBackToBaseType`, on the attribute or on
  [TomlPolymorphismOptions](cref:Meziantou.Framework.Toml.TomlPolymorphismOptions), to read it as the base type instead.

## Register derived types from another project

When the base type cannot reference its derived types, for example because they live in another project, register them
in the options:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/CrossProjectPolymorphismTests.cs;region=TomlPolymorphismOptions_DerivedTypeMappings;format=md-fenced-code-block;dedent=true -->
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
```
<!-- endref -->

With the source generator, register them on the context with [\[TomlDerivedTypeMapping\]](cref:Meziantou.Framework.Toml.Serialization.TomlDerivedTypeMappingAttribute):

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/CrossProjectPolymorphismTests.cs;region=TomlDerivedTypeMapping_Context;format=md-fenced-code-block;dedent=true -->
```csharp
[TomlSerializable(typeof(Animal))]
[TomlDerivedTypeMapping(typeof(Animal), typeof(Cat), "cat")]
[TomlDerivedTypeMapping(typeof(Animal), typeof(Dog), "dog")]
internal sealed partial class AnimalContext : TomlSerializerContext;
```
<!-- endref -->

The registrations are merged. [\[TomlDerivedType\]](cref:Meziantou.Framework.Toml.Serialization.TomlDerivedTypeAttribute) on the base type wins over the mappings of the context, which win
over the mappings of the options. When the base type of a mapping has no polymorphism configuration, such as [\[TomlPolymorphic\]](cref:Meziantou.Framework.Toml.Serialization.TomlPolymorphicAttribute),
the generator reports the warning `MFTOML010` and the default settings of the serializer are used.
