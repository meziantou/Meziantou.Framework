#pragma warning disable MA0048 // File name must match type name

using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

[YamlPolymorphic]
[YamlDerivedType(typeof(SubclassDog), "dog")]
internal abstract class SubclassPet
{
    public string Name { get; set; } = string.Empty;
}

internal class SubclassDog : SubclassPet
{
    public int BarkVolume { get; set; }
}

/// <summary>A subclass of a registered derived type that is not registered itself.</summary>
internal sealed class SubclassLabrador : SubclassDog
{
    public string Coat { get; set; } = string.Empty;
}

/// <summary>Registers the base of a hierarchy before its more derived type, so write dispatch order matters.</summary>
[YamlPolymorphic]
[YamlDerivedType(typeof(NestedDog), "dog")]
[YamlDerivedType(typeof(NestedLabrador), "labrador")]
internal abstract class NestedPet
{
    public string Name { get; set; } = string.Empty;
}

internal class NestedDog : NestedPet
{
    public int BarkVolume { get; set; }
}

internal sealed class NestedLabrador : NestedDog
{
    public string Coat { get; set; } = string.Empty;
}

[YamlSerializable(typeof(SubclassPet))]
[YamlSerializable(typeof(NestedPet))]
internal sealed partial class SubclassPetYamlContext : YamlSerializerContext
{
    public SubclassPetYamlContext()
    {
    }

    public SubclassPetYamlContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}

[YamlPolymorphic]
[YamlDerivedType(typeof(StrictCircle), "circle")]
internal abstract class StrictShape
{
    public string Name { get; set; } = string.Empty;
}

[YamlUnmappedMemberHandling(YamlUnmappedMemberHandling.Disallow)]
internal sealed class StrictCircle : StrictShape
{
    public int Radius { get; set; }
}

[YamlPolymorphic]
[YamlDerivedType(typeof(ExtensibleCircle), "circle")]
internal abstract class ExtensibleShape
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class ExtensibleCircle : ExtensibleShape
{
    [YamlExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}

/// <summary>A hierarchy where a registered derived type is polymorphic itself.</summary>
[YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Both)]
[YamlDerivedType(typeof(MultiLevelDog), "dog", Tag = "!dog")]
[YamlDerivedType(typeof(MultiLevelPuppy), "puppy", Tag = "!puppy")]
internal class MultiLevelAnimal
{
    public string Name { get; set; } = string.Empty;
}

[YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Both)]
[YamlDerivedType(typeof(MultiLevelPuppy), "puppy", Tag = "!puppy")]
internal class MultiLevelDog : MultiLevelAnimal
{
    public int BarkVolume { get; set; }
}

internal sealed class MultiLevelPuppy : MultiLevelDog
{
    public int AgeInWeeks { get; set; }
}

/// <summary>A concrete base type registering itself, so it has a discriminator too.</summary>
[YamlPolymorphic]
[YamlDerivedType(typeof(SelfRegisteredVehicle), "vehicle")]
[YamlDerivedType(typeof(SelfRegisteredCar), "car")]
internal class SelfRegisteredVehicle
{
    public int Wheels { get; set; }
}

internal sealed class SelfRegisteredCar : SelfRegisteredVehicle
{
    public int Doors { get; set; }
}

/// <summary>The discriminator property is not written with the tag style, so a member can use its name.</summary>
[YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag, TypeDiscriminatorPropertyName = "kind")]
[YamlDerivedType(typeof(TaggedKindDocument), Tag = "!doc")]
internal abstract class TaggedKindItem
{
}

internal sealed class TaggedKindDocument : TaggedKindItem
{
    [YamlPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;
}

[YamlPolymorphic]
[YamlDerivedType(typeof(KindPropertyDocument), "doc", Tag = "!doc")]
internal abstract class KindPropertyItem
{
}

internal sealed class KindPropertyDocument : KindPropertyItem
{
    [YamlPropertyName("$type")]
    public string Kind { get; set; } = string.Empty;
}

[YamlPolymorphic]
[YamlDerivedType(typeof(BoxedPoint), "point")]
internal interface IBoxedShape
{
}

internal struct BoxedPoint : IBoxedShape
{
    public int X { get; set; }
}

internal sealed class AliasedShapes
{
    public StrictShape? Shape { get; set; }

    public StrictShape? SameShape { get; set; }

    public ExtensibleShape? OtherShape { get; set; }
}

/// <summary>A hierarchy where a registered derived type registers a default derived type of its own.</summary>
[YamlPolymorphic]
[YamlDerivedType(typeof(NestedDefaultDocument), "document")]
internal class NestedDefaultItem
{
    public string Name { get; set; } = string.Empty;
}

[YamlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[YamlDerivedType(typeof(NestedDefaultReport))]
[YamlDerivedType(typeof(NestedDefaultInvoice), "invoice")]
internal class NestedDefaultDocument : NestedDefaultItem
{
    public int Pages { get; set; }
}

internal sealed class NestedDefaultReport : NestedDefaultDocument
{
    public string Author { get; set; } = string.Empty;
}

internal sealed class NestedDefaultInvoice : NestedDefaultDocument
{
    public int Total { get; set; }
}

/// <summary>An abstract registered derived type cannot be read as itself, so it keeps its default derived type.</summary>
[YamlPolymorphic]
[YamlDerivedType(typeof(NestedAbstractShape), "shape")]
internal abstract class NestedAbstractRoot
{
}

[YamlPolymorphic]
[YamlDerivedType(typeof(NestedAbstractSquare))]
internal abstract class NestedAbstractShape : NestedAbstractRoot
{
}

internal sealed class NestedAbstractSquare : NestedAbstractShape
{
    public int Side { get; set; }
}

[YamlSerializable(typeof(StrictShape))]
[YamlSerializable(typeof(ExtensibleShape))]
[YamlSerializable(typeof(NestedDefaultItem))]
[YamlSerializable(typeof(NestedAbstractRoot))]
[YamlSerializable(typeof(MultiLevelAnimal))]
[YamlSerializable(typeof(SelfRegisteredVehicle))]
[YamlSerializable(typeof(TaggedKindItem))]
[YamlSerializable(typeof(KindPropertyItem))]
[YamlSerializable(typeof(List<IBoxedShape>))]
[YamlSerializable(typeof(AliasedShapes))]
[YamlSerializable(typeof(Dictionary<string, StrictShape>))]
internal sealed partial class PolymorphismEdgeCaseYamlContext : YamlSerializerContext
{
    public PolymorphismEdgeCaseYamlContext()
    {
    }

    public PolymorphismEdgeCaseYamlContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}

public class YamlPolymorphismTests
{
    [YamlPolymorphic]
    [YamlDerivedType(typeof(Dog), "dog", Tag = "!dog")]
    [YamlDerivedType(typeof(Cat), "cat", Tag = "!cat")]
    private abstract class Animal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Dog : Animal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class Cat : Animal
    {
        public int Lives { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    [YamlDerivedType(typeof(JsonDog), "dog")]
    private abstract class JsonAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonDog : JsonAnimal
    {
        public int BarkVolume { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [YamlDerivedType(typeof(JsonDefaultCat), "cat")]
    [YamlDerivedType(typeof(JsonDefaultOther))]
    private abstract class JsonDefaultAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonDefaultCat : JsonDefaultAnimal
    {
        public int Lives { get; set; }
    }

    private sealed class JsonDefaultOther : JsonDefaultAnimal
    {
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(YamlDefaultDog), "dog")]
    [YamlDerivedType(typeof(YamlDefaultOther))]
    private abstract class YamlDefaultAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class YamlDefaultDog : YamlDefaultAnimal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class YamlDefaultOther : YamlDefaultAnimal
    {
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [YamlDerivedType(typeof(JsonIntDog), 1)]
    [YamlDerivedType(typeof(JsonIntCat), 2)]
    private abstract class JsonIntAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonIntDog : JsonIntAnimal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class JsonIntCat : JsonIntAnimal
    {
        public int Lives { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(YamlIntDog), 1)]
    [YamlDerivedType(typeof(YamlIntCat), 2)]
    private abstract class YamlIntAnimal
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class YamlIntDog : YamlIntAnimal
    {
        public int BarkVolume { get; set; }
    }

    private sealed class YamlIntCat : YamlIntAnimal
    {
        public int Lives { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(Circle), "circle")]
    private class Shape
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Circle : Shape
    {
        public double Radius { get; set; }
    }

    [YamlPolymorphic(UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
    [YamlDerivedType(typeof(YamlFallbackCircle), "circle")]
    private class YamlFallbackShape
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class YamlFallbackCircle : YamlFallbackShape
    {
        public double Radius { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$type", UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
    [YamlDerivedType(typeof(JsonOverriddenCircle), "circle")]
    private class JsonOverriddenShape
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonOverriddenCircle : JsonOverriddenShape
    {
        public double Radius { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$type", UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase)]
    [YamlDerivedType(typeof(JsonFallbackCircle), "circle")]
    private class JsonFallbackShape
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonFallbackCircle : JsonFallbackShape
    {
        public double Radius { get; set; }
    }

    [Fact]
    public void SerializeEmitsPropertyDiscriminatorFirst()
    {
        Animal animal = new Dog { Name = "Rex", BarkVolume = 3 };
        var yaml = YamlSerializer.Serialize(animal, typeof(Animal));

        var typeIndex = yaml.IndexOf("$type:", StringComparison.Ordinal);
        var nameIndex = yaml.IndexOf("Name:", StringComparison.Ordinal);
        Assert.True(typeIndex >= 0);
        Assert.True(nameIndex > typeIndex);
        Assert.Contains("$type: dog", yaml);
        Assert.Contains("BarkVolume: 3", yaml);
    }

    [Fact]
    public void DeserializeSelectsDerivedTypeWhenDiscriminatorNotFirst()
    {
        var yaml = "Name: Rex\nBarkVolume: 3\n$type: dog\n";
        var value = YamlSerializer.Deserialize<Animal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<Dog>(value);
        Assert.Equal("Rex", value.Name);
        Assert.Equal(3, ((Dog)value).BarkVolume);
    }

    [Fact]
    public void DeserializeSelectsDerivedTypeFromJsonPolymorphicAttributes()
    {
        var yaml = "Name: Rex\nBarkVolume: 3\n$kind: dog\n";
        var value = YamlSerializer.Deserialize<JsonAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonDog>(value);
        Assert.Equal("Rex", value.Name);
        Assert.Equal(3, ((JsonDog)value).BarkVolume);
    }

    [Fact]
    public void UnknownDiscriminatorFailsByDefault()
    {
        var yaml = "Name: Rex\n$type: lizard\n";
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Animal>(yaml));
    }

    [Fact]
    public void UnknownDiscriminatorCanFallBackToBase()
    {
        var yaml = "Name: Base\n$type: unknown\n";
        var value = YamlSerializer.Deserialize<Shape>(
            yaml,
            new YamlSerializerOptions
            {
                PolymorphismOptions = new YamlPolymorphismOptions
                {
                    UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase,
                },
            });

        Assert.NotNull(value);
        Assert.IsType<Shape>(value);
        Assert.Equal("Base", value.Name);
    }

    [Fact]
    public void YamlPolymorphicAttributeUnknownHandlingFallsBackToBase()
    {
        var yaml = "Name: Base\n$type: unknown\n";
        var value = YamlSerializer.Deserialize<YamlFallbackShape>(yaml);

        Assert.NotNull(value);
        Assert.IsType<YamlFallbackShape>(value);
        Assert.Equal("Base", value.Name);
    }

    [Fact]
    public void YamlPolymorphicAttributeUnknownHandlingOverridesJsonAttribute()
    {
        // YamlPolymorphic sets UnknownDerivedTypeHandling to FallBackToBase
        var yaml = "Name: Base\n$type: unknown\n";
        var value = YamlSerializer.Deserialize<JsonOverriddenShape>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonOverriddenShape>(value);
        Assert.Equal("Base", value.Name);
    }

    [Fact]
    public void TagDiscriminatorCanBeUsedWhenEnabled()
    {
        var yaml = "!dog\nName: Rex\nBarkVolume: 3\n";
        var value = YamlSerializer.Deserialize<Animal>(
            yaml,
            new YamlSerializerOptions
            {
                PolymorphismOptions = new YamlPolymorphismOptions
                {
                    DiscriminatorStyle = YamlTypeDiscriminatorStyle.Both,
                },
            });

        Assert.NotNull(value);
        Assert.IsType<Dog>(value);
        Assert.Equal(3, ((Dog)value).BarkVolume);
    }

    [Fact]
    public void SerializeCanEmitTagDiscriminatorOnly()
    {
        Animal animal = new Cat { Name = "Mittens", Lives = 9 };
        var yaml = YamlSerializer.Serialize(
            animal,
            typeof(Animal),
            new YamlSerializerOptions
            {
                PolymorphismOptions = new YamlPolymorphismOptions
                {
                    DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                },
            });

        Assert.Contains("!cat", yaml);
        Assert.DoesNotContain("$type:", yaml);
        Assert.Contains("Lives: 9", yaml);
    }

    [Fact]
    public void JsonDefaultDerivedTypeDeserializesWhenDiscriminatorIsMissing()
    {
        var yaml = "Name: Cupcake\n";
        var value = YamlSerializer.Deserialize<JsonDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonDefaultOther>(value);
        Assert.Equal("Cupcake", value.Name);
    }

    [Fact]
    public void JsonDefaultDerivedTypeDeserializesWhenDiscriminatorMatches()
    {
        var yaml = "type: cat\nName: Biscuit\nLives: 7\n";
        var value = YamlSerializer.Deserialize<JsonDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonDefaultCat>(value);
        Assert.Equal("Biscuit", value.Name);
        Assert.Equal(7, ((JsonDefaultCat)value).Lives);
    }

    [Fact]
    public void JsonDefaultDerivedTypeDeserializesWhenDiscriminatorIsUnknown()
    {
        var yaml = "type: lizard\nName: Gex\n";
        var value = YamlSerializer.Deserialize<JsonDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonDefaultOther>(value);
        Assert.Equal("Gex", value.Name);
    }

    [Fact]
    public void JsonDefaultDerivedTypeSerializesWithoutDiscriminator()
    {
        JsonDefaultAnimal animal = new JsonDefaultOther { Name = "Cupcake" };
        var yaml = YamlSerializer.Serialize(animal, typeof(JsonDefaultAnimal));

        Assert.DoesNotContain("type:", yaml);
        Assert.Contains("Name: Cupcake", yaml);
    }

    [Fact]
    public void JsonDefaultDerivedTypeSerializesWithDiscriminatorForNonDefaultType()
    {
        JsonDefaultAnimal animal = new JsonDefaultCat { Name = "Biscuit", Lives = 7 };
        var yaml = YamlSerializer.Serialize(animal, typeof(JsonDefaultAnimal));

        Assert.Contains("type: cat", yaml);
        Assert.Contains("Name: Biscuit", yaml);
    }

    [Fact]
    public void YamlDefaultDerivedTypeDeserializesWhenDiscriminatorIsMissing()
    {
        var yaml = "Name: Cupcake\n";
        var value = YamlSerializer.Deserialize<YamlDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<YamlDefaultOther>(value);
        Assert.Equal("Cupcake", value.Name);
    }

    [Fact]
    public void YamlDefaultDerivedTypeDeserializesWhenDiscriminatorMatches()
    {
        var yaml = "$type: dog\nName: Rex\nBarkVolume: 5\n";
        var value = YamlSerializer.Deserialize<YamlDefaultAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<YamlDefaultDog>(value);
        Assert.Equal("Rex", value.Name);
        Assert.Equal(5, ((YamlDefaultDog)value).BarkVolume);
    }

    [Fact]
    public void YamlDefaultDerivedTypeSerializesWithoutDiscriminator()
    {
        YamlDefaultAnimal animal = new YamlDefaultOther { Name = "Cupcake" };
        var yaml = YamlSerializer.Serialize(animal, typeof(YamlDefaultAnimal));

        Assert.DoesNotContain("$type:", yaml);
        Assert.Contains("Name: Cupcake", yaml);
    }

    [Fact]
    public void YamlDefaultDerivedTypeSerializesWithDiscriminatorForNonDefaultType()
    {
        YamlDefaultAnimal animal = new YamlDefaultDog { Name = "Rex", BarkVolume = 5 };
        var yaml = YamlSerializer.Serialize(animal, typeof(YamlDefaultAnimal));

        Assert.Contains("$type: dog", yaml);
        Assert.Contains("Name: Rex", yaml);
    }

    [Fact]
    public void JsonIntDiscriminatorSerializesCorrectly()
    {
        JsonIntAnimal animal = new JsonIntDog { Name = "Rex", BarkVolume = 3 };
        var yaml = YamlSerializer.Serialize(animal, typeof(JsonIntAnimal));

        Assert.Contains("$type: 1", yaml);
        Assert.Contains("BarkVolume: 3", yaml);
    }

    [Fact]
    public void JsonIntDiscriminatorDeserializesCorrectly()
    {
        var yaml = "$type: 2\nName: Mittens\nLives: 9\n";
        var value = YamlSerializer.Deserialize<JsonIntAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonIntCat>(value);
        Assert.Equal("Mittens", value.Name);
        Assert.Equal(9, ((JsonIntCat)value).Lives);
    }

    [Fact]
    public void YamlIntDiscriminatorSerializesCorrectly()
    {
        YamlIntAnimal animal = new YamlIntDog { Name = "Rex", BarkVolume = 3 };
        var yaml = YamlSerializer.Serialize(animal, typeof(YamlIntAnimal));

        Assert.Contains("$type: 1", yaml);
        Assert.Contains("BarkVolume: 3", yaml);
    }

    [Fact]
    public void YamlIntDiscriminatorDeserializesCorrectly()
    {
        var yaml = "$type: 2\nName: Mittens\nLives: 9\n";
        var value = YamlSerializer.Deserialize<YamlIntAnimal>(yaml);

        Assert.NotNull(value);
        Assert.IsType<YamlIntCat>(value);
        Assert.Equal("Mittens", value.Name);
        Assert.Equal(9, ((YamlIntCat)value).Lives);
    }

    [Fact]
    public void JsonPolymorphicAttributeUnknownHandlingFallsBackToBase()
    {
        var yaml = "Name: Base\n$type: unknown\n";
        var value = YamlSerializer.Deserialize<JsonFallbackShape>(yaml);

        Assert.NotNull(value);
        Assert.IsType<JsonFallbackShape>(value);
        Assert.Equal("Base", value.Name);
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(GenericDog<>), "dog")]
    private abstract class GenericAnimal<T>
    {
        public T? Name { get; set; }
    }

    private sealed class GenericDog<T> : GenericAnimal<T>
    {
        public int BarkVolume { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(SwappedPair<,>), "swapped")]
    private abstract class PairBase<TFirst, TSecond>
    {
    }

    private sealed class SwappedPair<TSecond, TFirst> : PairBase<TFirst, TSecond>
    {
        public TFirst? First { get; set; }

        public TSecond? Second { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(PartiallyClosed<>), "partial")]
    private abstract class PartiallyClosedBase<TFirst, TSecond>
    {
    }

    private sealed class PartiallyClosed<T> : PartiallyClosedBase<T, int>
    {
        public T? Value { get; set; }
    }

    private sealed class PartiallyClosedNonMatching : PartiallyClosedBase<string, bool>
    {
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(WrappedList<>), "wrapped")]
    private abstract class WrapperBase<T>
    {
    }

    private sealed class WrappedList<T> : WrapperBase<List<T>>
    {
        public int Count { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(WrappedArray<>), "array")]
    private abstract class ArrayWrapperBase<T>
    {
    }

    private sealed class WrappedArray<T> : ArrayWrapperBase<T[]>
    {
        public int Length { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(NodeImplementation<>), "impl")]
    private interface INode<T>
    {
    }

    private sealed class NodeImplementation<T> : INode<T>
    {
        public T? Value { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(NestingOuter<>.Leaf<>), "leaf")]
    private abstract class NestedBase<T>
    {
    }

    private static class NestingOuter<TOuter>
    {
        internal sealed class Leaf<TInner> : NestedBase<KeyValuePair<TOuter, TInner>>
        {
            public string Label { get; set; } = string.Empty;
        }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(AmbiguousDerived<>), "ambiguous")]
    private interface IAmbiguousBase<T>
    {
    }

    private sealed class AmbiguousDerived<T> : IAmbiguousBase<T>, IAmbiguousBase<List<T>>
    {
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(UnboundDerived<,>), "unbound")]
    private abstract class UnboundBase<T>
    {
    }

    private sealed class UnboundDerived<TFirst, TSecond> : UnboundBase<TFirst>
    {
    }

    [Fact]
    public void OpenGenericDerivedTypeUsesMatchingArity()
    {
        GenericAnimal<string> animal = new GenericDog<string> { Name = "Rex", BarkVolume = 3 };
        var yaml = YamlSerializer.Serialize(animal, typeof(GenericAnimal<string>));

        Assert.Contains("$type: dog", yaml);
        Assert.Contains("BarkVolume: 3", yaml);

        var value = YamlSerializer.Deserialize<GenericAnimal<string>>(yaml);

        Assert.NotNull(value);
        Assert.IsType<GenericDog<string>>(value);
        Assert.Equal("Rex", value.Name);
    }

    [Fact]
    public void OpenGenericDerivedTypeIsResolvedForEachBaseInstantiation()
    {
        GenericAnimal<int> animal = new GenericDog<int> { Name = 1, BarkVolume = 4 };
        var yaml = YamlSerializer.Serialize(animal, typeof(GenericAnimal<int>));

        Assert.Contains("$type: dog", yaml);

        var value = YamlSerializer.Deserialize<GenericAnimal<int>>(yaml);

        Assert.NotNull(value);
        Assert.IsType<GenericDog<int>>(value);
        Assert.Equal(1, value.Name);
    }

    [Fact]
    public void OpenGenericDerivedTypeSupportsReorderedTypeParameters()
    {
        PairBase<int, string> pair = new SwappedPair<string, int> { First = 1, Second = "two" };
        var yaml = YamlSerializer.Serialize(pair, typeof(PairBase<int, string>));

        Assert.Contains("$type: swapped", yaml);

        var value = YamlSerializer.Deserialize<PairBase<int, string>>(yaml);

        Assert.IsType<SwappedPair<string, int>>(value);
    }

    [Fact]
    public void OpenGenericDerivedTypeSupportsPartiallyClosedBase()
    {
        PartiallyClosedBase<string, int> value = new PartiallyClosed<string> { Value = "text" };
        var yaml = YamlSerializer.Serialize(value, typeof(PartiallyClosedBase<string, int>));

        Assert.Contains("$type: partial", yaml);
        Assert.IsType<PartiallyClosed<string>>(YamlSerializer.Deserialize<PartiallyClosedBase<string, int>>(yaml));
    }

    [Fact]
    public void OpenGenericDerivedTypeIsRejectedWhenGroundArgumentDoesNotMatch()
    {
        PartiallyClosedBase<string, bool> value = new PartiallyClosedNonMatching();

        var exception = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Serialize(value, typeof(PartiallyClosedBase<string, bool>)));

        Assert.Contains("cannot be resolved", exception.Message);
    }

    [Fact]
    public void OpenGenericDerivedTypeSupportsWrappedTypeArguments()
    {
        WrapperBase<List<int>> value = new WrappedList<int> { Count = 2 };
        var yaml = YamlSerializer.Serialize(value, typeof(WrapperBase<List<int>>));

        Assert.Contains("$type: wrapped", yaml);
        Assert.IsType<WrappedList<int>>(YamlSerializer.Deserialize<WrapperBase<List<int>>>(yaml));
    }

    [Fact]
    public void OpenGenericDerivedTypeSupportsArrayTypeArguments()
    {
        ArrayWrapperBase<int[]> value = new WrappedArray<int> { Length = 3 };
        var yaml = YamlSerializer.Serialize(value, typeof(ArrayWrapperBase<int[]>));

        Assert.Contains("$type: array", yaml);
        Assert.IsType<WrappedArray<int>>(YamlSerializer.Deserialize<ArrayWrapperBase<int[]>>(yaml));
    }

    [Fact]
    public void OpenGenericDerivedTypeSupportsInterfaceBase()
    {
        INode<int> value = new NodeImplementation<int> { Value = 7 };
        var yaml = YamlSerializer.Serialize(value, typeof(INode<int>));

        Assert.Contains("$type: impl", yaml);
        Assert.IsType<NodeImplementation<int>>(YamlSerializer.Deserialize<INode<int>>(yaml));
    }

    [Fact]
    public void OpenGenericDerivedTypeSupportsTypeParametersFromEnclosingTypes()
    {
        NestedBase<KeyValuePair<string, int>> value = new NestingOuter<string>.Leaf<int> { Label = "leaf" };
        var yaml = YamlSerializer.Serialize(value, typeof(NestedBase<KeyValuePair<string, int>>));

        Assert.Contains("$type: leaf", yaml);
        Assert.IsType<NestingOuter<string>.Leaf<int>>(YamlSerializer.Deserialize<NestedBase<KeyValuePair<string, int>>>(yaml));
    }

    [Fact]
    public void OpenGenericDerivedTypeThrowsWhenAmbiguous()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Serialize(new AmbiguousDerived<List<int>>(), typeof(IAmbiguousBase<List<List<int>>>)));

        Assert.Contains("ambiguous", exception.Message);
    }

    [Fact]
    public void OpenGenericDerivedTypeThrowsWhenTypeArgumentCannotBeInferred()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Serialize(new UnboundDerived<int, string>(), typeof(UnboundBase<int>)));

        Assert.Contains("cannot be resolved", exception.Message);
    }

    private static YamlSerializerOptions InferClosedTypePolymorphismOptions { get; } = new()
    {
        PolymorphismOptions = new YamlPolymorphismOptions { InferClosedTypePolymorphism = true },
    };

    private closed class ClosedShape
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ClosedCircle : ClosedShape
    {
        public double Radius { get; set; }
    }

    private sealed class ClosedSquare : ClosedShape
    {
        public double Side { get; set; }
    }

    private closed class ClosedEmptyBase;

    private abstract class PlainAbstractBase
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PlainAbstractDerived : PlainAbstractBase;

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    private closed class ClosedCustomDiscriminatorBase
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ClosedCustomDiscriminatorDerived : ClosedCustomDiscriminatorBase
    {
        public int Value { get; set; }
    }

    [YamlPolymorphic]
    [YamlDerivedType(typeof(ClosedExplicitDerived), "custom")]
    private closed class ClosedExplicitBase
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ClosedExplicitDerived : ClosedExplicitBase
    {
        public int Value { get; set; }
    }

    [YamlPolymorphic(InferClosedTypePolymorphism = true)]
    private closed class ClosedOptInBase
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ClosedOptInDerived : ClosedOptInBase
    {
        public int Value { get; set; }
    }

    [YamlPolymorphic(InferClosedTypePolymorphism = false)]
    private closed class ClosedOptOutBase
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ClosedOptOutDerived : ClosedOptOutBase
    {
        public int Value { get; set; }
    }

    [YamlPolymorphic(InferClosedTypePolymorphism = true)]
    private abstract class NonClosedOptInBase
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class NonClosedOptInDerived : NonClosedOptInBase
    {
    }

    private closed class ClosedContainer<T>
    {
        public T? BaseValue { get; set; }
    }

    private sealed class ClosedBox<T> : ClosedContainer<T>
    {
        public T? Value { get; set; }
    }

    private sealed class ClosedIntBag : ClosedContainer<int>
    {
        public int Count { get; set; }
    }

    internal closed class ClosedLessVisibleBase
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ClosedLessVisibleDerived : ClosedLessVisibleBase
    {
    }

    [Fact]
    public void ClosedTypeInferenceEmitsAndReadsDiscriminatorFromTypeName()
    {
        ClosedShape value = new ClosedCircle { Name = "circle", Radius = 3 };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedShape), InferClosedTypePolymorphismOptions);

        Assert.Contains("$type: ClosedCircle", yaml);

        var roundtripped = YamlSerializer.Deserialize<ClosedShape>(yaml, InferClosedTypePolymorphismOptions);
        var circle = Assert.IsType<ClosedCircle>(roundtripped);
        Assert.Equal("circle", circle.Name);
        Assert.Equal(3, circle.Radius);
    }

    [Fact]
    public void ClosedTypeInferenceRegistersEveryDerivedType()
    {
        ClosedShape value = new ClosedSquare { Name = "square", Side = 4 };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedShape), InferClosedTypePolymorphismOptions);

        Assert.Contains("$type: ClosedSquare", yaml);
        Assert.IsType<ClosedSquare>(YamlSerializer.Deserialize<ClosedShape>(yaml, InferClosedTypePolymorphismOptions));
    }

    [Fact]
    public void ClosedTypeInferenceIsDisabledByDefault()
    {
        ClosedShape value = new ClosedCircle { Name = "circle", Radius = 3 };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedShape));

        Assert.DoesNotContain("$type", yaml);
    }

    [Fact]
    public void ClosedTypeInferenceIgnoresClosedTypeWithoutDerivedType()
    {
        var yaml = YamlSerializer.Serialize<ClosedEmptyBase?>(null, InferClosedTypePolymorphismOptions);

        Assert.DoesNotContain("$type", yaml);
    }

    [Fact]
    public void ClosedTypeInferenceIsNotAppliedToPlainAbstractClass()
    {
        PlainAbstractBase value = new PlainAbstractDerived { Name = "plain" };
        var yaml = YamlSerializer.Serialize(value, typeof(PlainAbstractBase), InferClosedTypePolymorphismOptions);

        Assert.DoesNotContain("$type", yaml);
    }

    [Fact]
    public void ClosedTypeInferenceHonorsCustomDiscriminatorPropertyName()
    {
        ClosedCustomDiscriminatorBase value = new ClosedCustomDiscriminatorDerived { Name = "custom", Value = 42 };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedCustomDiscriminatorBase), InferClosedTypePolymorphismOptions);

        Assert.Contains("$kind: ClosedCustomDiscriminatorDerived", yaml);
        Assert.IsType<ClosedCustomDiscriminatorDerived>(YamlSerializer.Deserialize<ClosedCustomDiscriminatorBase>(yaml, InferClosedTypePolymorphismOptions));
    }

    [Fact]
    public void ClosedTypeInferenceIsSuppressedByExplicitDerivedTypes()
    {
        ClosedExplicitBase value = new ClosedExplicitDerived { Name = "explicit", Value = 42 };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedExplicitBase), InferClosedTypePolymorphismOptions);

        Assert.Contains("$type: custom", yaml);
    }

    [Fact]
    public void ClosedTypeInferenceIsSuppressedByRuntimeDerivedTypeMappings()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                InferClosedTypePolymorphism = true,
                DerivedTypeMappings =
                {
                    [typeof(ClosedShape)] = [new YamlDerivedType(typeof(ClosedCircle), "runtime-circle")],
                },
            },
        };

        ClosedShape value = new ClosedCircle { Name = "circle", Radius = 3 };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedShape), options);

        Assert.Contains("$type: runtime-circle", yaml);
    }

    [Fact]
    public void ClosedTypeInferenceCanBeEnabledPerType()
    {
        ClosedOptInBase value = new ClosedOptInDerived { Name = "opt-in", Value = 42 };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedOptInBase));

        Assert.Contains("$type: ClosedOptInDerived", yaml);
        Assert.IsType<ClosedOptInDerived>(YamlSerializer.Deserialize<ClosedOptInBase>(yaml));
    }

    [Fact]
    public void ClosedTypeInferenceCanBeDisabledPerType()
    {
        ClosedOptOutBase value = new ClosedOptOutDerived { Name = "opt-out", Value = 42 };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedOptOutBase), InferClosedTypePolymorphismOptions);

        Assert.DoesNotContain("$type", yaml);
    }

    [Fact]
    public void ClosedTypeInferenceThrowsWhenEnabledOnNonClosedType()
    {
        NonClosedOptInBase value = new NonClosedOptInDerived { Name = "plain" };
        var exception = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Serialize(value, typeof(NonClosedOptInBase)));

        Assert.Contains("is not a closed type", exception.Message);
    }

    [Fact]
    public void ClosedTypeInferenceThrowsWhenDerivedTypeIsLessVisibleThanBaseType()
    {
        ClosedLessVisibleBase value = new ClosedLessVisibleDerived { Name = "hidden" };
        var exception = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Serialize(value, typeof(ClosedLessVisibleBase), InferClosedTypePolymorphismOptions));

        Assert.Contains("less visible", exception.Message);
    }

    [Fact]
    public void ClosedTypeInferenceResolvesGenericDerivedTypes()
    {
        ClosedContainer<int> value = new ClosedBox<int> { BaseValue = 7, Value = 42 };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedContainer<int>), InferClosedTypePolymorphismOptions);

        Assert.Contains("$type: ClosedBox", yaml);
        Assert.IsType<ClosedBox<int>>(YamlSerializer.Deserialize<ClosedContainer<int>>(yaml, InferClosedTypePolymorphismOptions));

        ClosedContainer<int> bag = new ClosedIntBag { BaseValue = 3, Count = 2 };
        var bagYaml = YamlSerializer.Serialize(bag, typeof(ClosedContainer<int>), InferClosedTypePolymorphismOptions);

        Assert.Contains("$type: ClosedIntBag", bagYaml);
        Assert.IsType<ClosedIntBag>(YamlSerializer.Deserialize<ClosedContainer<int>>(bagYaml, InferClosedTypePolymorphismOptions));
    }

    private closed class ClosedNestedRoot
    {
        public string Name { get; set; } = string.Empty;
    }

    private closed class ClosedNestedMiddle : ClosedNestedRoot
    {
        public int Level { get; set; }
    }

    private sealed class ClosedNestedLeaf : ClosedNestedMiddle
    {
        public string Detail { get; set; } = string.Empty;
    }

    [Fact]
    public void ClosedTypeInferenceRegistersTransitiveDerivedTypes()
    {
        ClosedNestedRoot value = new ClosedNestedLeaf { Name = "leaf", Level = 2, Detail = "detail" };
        var rootYaml = YamlSerializer.Serialize(value, typeof(ClosedNestedRoot), InferClosedTypePolymorphismOptions);

        Assert.Contains("$type: ClosedNestedLeaf", rootYaml);
        var leaf = Assert.IsType<ClosedNestedLeaf>(YamlSerializer.Deserialize<ClosedNestedRoot>(rootYaml, InferClosedTypePolymorphismOptions));
        Assert.Equal("leaf", leaf.Name);
        Assert.Equal(2, leaf.Level);
        Assert.Equal("detail", leaf.Detail);

        ClosedNestedMiddle middleValue = new ClosedNestedLeaf { Name = "leaf", Level = 2, Detail = "detail" };
        var yaml = YamlSerializer.Serialize(middleValue, typeof(ClosedNestedMiddle), InferClosedTypePolymorphismOptions);

        Assert.Contains("$type: ClosedNestedLeaf", yaml);
    }

    [Fact]
    public void ClosedTypeInferenceFailsForUnknownDiscriminator()
    {
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<ClosedShape>("$type: Nonexistent\n", InferClosedTypePolymorphismOptions));
    }

    private closed class ClosedPet
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ClosedCat : ClosedPet
    {
        public bool Indoor { get; set; }
    }

    private closed class ClosedDog : ClosedPet
    {
        public bool GoodBoy { get; set; }
    }

    private sealed class ClosedLabrador : ClosedDog
    {
        public string Color { get; set; } = string.Empty;
    }

    private sealed class ClosedCollie : ClosedDog
    {
        public bool Herding { get; set; }
    }

    [Fact]
    public void ClosedTypeInferenceRegistersDescendantsOfNestedClosedTypes()
    {
        ClosedPet value = new ClosedLabrador { Name = "Rex", GoodBoy = true, Color = "chocolate" };
        var yaml = YamlSerializer.Serialize(value, typeof(ClosedPet), InferClosedTypePolymorphismOptions);

        Assert.Contains("$type: ClosedLabrador", yaml);

        var labrador = Assert.IsType<ClosedLabrador>(YamlSerializer.Deserialize<ClosedPet>(yaml, InferClosedTypePolymorphismOptions));
        Assert.Equal("Rex", labrador.Name);
        Assert.True(labrador.GoodBoy);
        Assert.Equal("chocolate", labrador.Color);
    }

    [Fact]
    public void ClosedTypeInferenceRegistersEveryDescendantOfANestedClosedHierarchy()
    {
        ClosedPet collie = new ClosedCollie { Name = "Lassie", GoodBoy = true, Herding = true };
        Assert.Contains("$type: ClosedCollie", YamlSerializer.Serialize(collie, typeof(ClosedPet), InferClosedTypePolymorphismOptions));

        ClosedPet cat = new ClosedCat { Name = "Mittens", Indoor = true };
        Assert.Contains("$type: ClosedCat", YamlSerializer.Serialize(cat, typeof(ClosedPet), InferClosedTypePolymorphismOptions));

        ClosedDog dog = new ClosedLabrador { Name = "Rex", Color = "black" };
        Assert.Contains("$type: ClosedLabrador", YamlSerializer.Serialize(dog, typeof(ClosedDog), InferClosedTypePolymorphismOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritingAnUnregisteredSubclassOfARegisteredDerivedTypeThrows(bool useSourceGeneration)
    {
        SubclassPet value = new SubclassLabrador { Name = "Rex", BarkVolume = 3, Coat = "yellow" };

        var exception = Assert.Throws<NotSupportedException>(() => Serialize(value, useSourceGeneration));

        Assert.Contains(typeof(SubclassLabrador).ToString(), exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritingARegisteredDerivedTypeUsesItsOwnDiscriminator(bool useSourceGeneration)
    {
        SubclassPet value = new SubclassDog { Name = "Rex", BarkVolume = 3 };

        var yaml = Serialize(value, useSourceGeneration);

        Assert.Contains("$type: dog", yaml);
        Assert.Contains("BarkVolume: 3", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritingARegisteredDerivedTypeIsNotShadowedByItsRegisteredBase(bool useSourceGeneration)
    {
        NestedPet value = new NestedLabrador { Name = "Rex", BarkVolume = 3, Coat = "yellow" };

        var yaml = useSourceGeneration
            ? YamlSerializer.Serialize(value, typeof(NestedPet), new SubclassPetYamlContext())
            : YamlSerializer.Serialize(value, typeof(NestedPet));

        Assert.Contains("$type: labrador", yaml);
        Assert.Contains("Coat: yellow", yaml);

        var roundtripped = useSourceGeneration
            ? YamlSerializer.Deserialize(yaml, typeof(NestedPet), new SubclassPetYamlContext())
            : YamlSerializer.Deserialize<NestedPet>(yaml);
        var labrador = Assert.IsType<NestedLabrador>(roundtripped);
        Assert.Equal("Rex", labrador.Name);
        Assert.Equal(3, labrador.BarkVolume);
        Assert.Equal("yellow", labrador.Coat);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadingADerivedTypeDoesNotReportTheDiscriminatorAsAnUnmappedMember(bool useSourceGeneration)
    {
        StrictShape value = new StrictCircle { Name = "c", Radius = 3 };

        var yaml = SerializeEdgeCase(value, useSourceGeneration);
        var circle = Assert.IsType<StrictCircle>(DeserializeEdgeCase<StrictShape>(yaml, useSourceGeneration));

        Assert.Equal("$type: circle\nName: c\nRadius: 3\n", yaml);
        Assert.Equal("c", circle.Name);
        Assert.Equal(3, circle.Radius);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadingADerivedTypeDoesNotCaptureTheDiscriminatorInExtensionData(bool useSourceGeneration)
    {
        var circle = Assert.IsType<ExtensibleCircle>(DeserializeEdgeCase<ExtensibleShape>("Name: c\n$type: circle\nColor: red\n", useSourceGeneration));

        Assert.NotNull(circle.Extra);
        Assert.Equal(["Color"], circle.Extra.Keys);
        Assert.Equal("$type: circle\nName: c\nColor: red\n", SerializeEdgeCase<ExtensibleShape>(circle, useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritingDoesNotDuplicateTheDiscriminatorFromExtensionData(bool useSourceGeneration)
    {
        ExtensibleShape value = new ExtensibleCircle { Name = "c", Extra = new() { ["$type"] = "square", ["Color"] = "red" } };

        Assert.Equal("$type: circle\nName: c\nColor: red\n", SerializeEdgeCase(value, useSourceGeneration));
    }

    [Theory]
    [InlineData(false, "$type: dog\nName: Rex\nBarkVolume: 3\n")]
    [InlineData(true, "$type: dog\nName: Rex\nBarkVolume: 3\n")]
    [InlineData(false, "!dog\nName: Rex\nBarkVolume: 3\n")]
    [InlineData(true, "!dog\nName: Rex\nBarkVolume: 3\n")]
    public void ReadingADerivedTypeThatIsPolymorphicItselfConsumesTheDiscriminatorOnce(bool useSourceGeneration, string yaml)
    {
        var dog = Assert.IsType<MultiLevelDog>(DeserializeEdgeCase<MultiLevelAnimal>(yaml, useSourceGeneration));

        Assert.Equal("Rex", dog.Name);
        Assert.Equal(3, dog.BarkVolume);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADerivedTypeThatIsPolymorphicItselfRoundTrips(bool useSourceGeneration)
    {
        MultiLevelAnimal value = new MultiLevelDog { Name = "Rex", BarkVolume = 3 };

        var yaml = SerializeEdgeCase(value, useSourceGeneration);
        var dog = Assert.IsType<MultiLevelDog>(DeserializeEdgeCase<MultiLevelAnimal>(yaml, useSourceGeneration));

        Assert.Equal("!dog\n$type: dog\nName: Rex\nBarkVolume: 3\n", yaml);
        Assert.Equal(3, dog.BarkVolume);
        Assert.IsType<MultiLevelPuppy>(DeserializeEdgeCase<MultiLevelAnimal>("!puppy\n$type: puppy\nAgeInWeeks: 2\n", useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritingARegisteredBaseTypeEmitsItsDiscriminator(bool useSourceGeneration)
    {
        var yaml = SerializeEdgeCase(new SelfRegisteredVehicle { Wheels = 2 }, useSourceGeneration);
        var value = DeserializeEdgeCase<SelfRegisteredVehicle>(yaml, useSourceGeneration);

        Assert.Equal("$type: vehicle\nWheels: 2\n", yaml);
        Assert.Equal(typeof(SelfRegisteredVehicle), value!.GetType());
        Assert.Equal(2, value.Wheels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TagStyleWritesAMemberNamedLikeTheDiscriminatorProperty(bool useSourceGeneration)
    {
        TaggedKindItem value = new TaggedKindDocument { Kind = "report" };

        var yaml = SerializeEdgeCase(value, useSourceGeneration);
        var document = Assert.IsType<TaggedKindDocument>(DeserializeEdgeCase<TaggedKindItem>(yaml, useSourceGeneration));

        Assert.Equal("!doc\nkind: report\n", yaml);
        Assert.Equal("report", document.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TagStyleFromOptionsWritesAMemberNamedLikeTheDiscriminatorProperty(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { PolymorphismOptions = new YamlPolymorphismOptions { DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag } };
        KindPropertyItem value = new KindPropertyDocument { Kind = "report" };

        var yaml = SerializeEdgeCase(value, useSourceGeneration, options);
        var document = Assert.IsType<KindPropertyDocument>(DeserializeEdgeCase<KindPropertyItem>(yaml, useSourceGeneration, options));

        Assert.Equal("!doc\n$type: report\n", yaml);
        Assert.Equal("report", document.Kind);
    }

    [Theory]
    [InlineData(false, "first:\n  Name: c\n", "(Lin: 1, Col: 2, Chr: 9) - (Lin: 1, Col: 2, Chr: 9): Cannot deserialize abstract type 'Meziantou.Framework.Yaml.Tests.Serialization.StrictShape' without a known derived type discriminator.")]
    [InlineData(true, "first:\n  Name: c\n", "(Lin: 1, Col: 2, Chr: 9) - (Lin: 1, Col: 2, Chr: 9): Cannot deserialize abstract type 'Meziantou.Framework.Yaml.Tests.Serialization.StrictShape' without a known derived type discriminator.")]
    [InlineData(false, "first:\n  $type: square\n", "(Lin: 1, Col: 2, Chr: 9) - (Lin: 1, Col: 2, Chr: 9): Unknown type discriminator 'square' for 'Meziantou.Framework.Yaml.Tests.Serialization.StrictShape'.")]
    [InlineData(true, "first:\n  $type: square\n", "(Lin: 1, Col: 2, Chr: 9) - (Lin: 1, Col: 2, Chr: 9): Unknown type discriminator 'square' for 'Meziantou.Framework.Yaml.Tests.Serialization.StrictShape'.")]
    public void ReadingAPolymorphicTypeReportsTheErrorAtThePolymorphicNode(bool useSourceGeneration, string yaml, string expectedMessage)
    {
        var exception = Assert.Throws<YamlException>(() => DeserializeEdgeCase<Dictionary<string, StrictShape>>(yaml, useSourceGeneration));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoxedStructsAreNotAnchored(bool useSourceGeneration)
    {
        IBoxedShape point = new BoxedPoint { X = 1 };
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.PreserveMinimal };

        var yaml = SerializeEdgeCase(new List<IBoxedShape> { point, point }, useSourceGeneration, options);

        Assert.Equal("- $type: point\n  X: 1\n- $type: point\n  X: 1\n", yaml);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAliasToAnIncompatibleTypeThrowsAYamlException(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

        var exception = Assert.Throws<YamlException>(() => DeserializeEdgeCase<AliasedShapes>("Shape: &shape\n  $type: circle\nOtherShape: *shape\n", useSourceGeneration, options));

        Assert.IsType<InvalidCastException>(exception.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAnchoredDerivedTypeWithOnlyADiscriminatorCanBeAliased(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

        var value = DeserializeEdgeCase<AliasedShapes>("Shape: &shape\n  $type: circle\nSameShape: *shape\n", useSourceGeneration, options);

        Assert.NotNull(value);
        Assert.Same(Assert.IsType<StrictCircle>(value.Shape), value.SameShape);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadingADerivedTypeSelectedByItsDiscriminatorIgnoresItsOwnDefaultDerivedType(bool useSourceGeneration)
    {
        var value = DeserializeEdgeCase<NestedDefaultItem>("$type: document\nName: n\nPages: 2\n", useSourceGeneration);

        Assert.Equal(typeof(NestedDefaultDocument), value!.GetType());
        Assert.Equal("n", value.Name);
        Assert.Equal(2, ((NestedDefaultDocument)value).Pages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADerivedTypeWithItsOwnDefaultDerivedTypeRoundTrips(bool useSourceGeneration)
    {
        NestedDefaultItem value = new NestedDefaultDocument { Name = "n", Pages = 2 };

        var yaml = SerializeEdgeCase(value, useSourceGeneration);
        var roundTripped = DeserializeEdgeCase<NestedDefaultItem>(yaml, useSourceGeneration);

        Assert.Equal("$type: document\nName: n\nPages: 2\n", yaml);
        Assert.Equal(typeof(NestedDefaultDocument), roundTripped!.GetType());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADerivedTypeSelectedByItsDiscriminatorStillReadsItsOwnDiscriminator(bool useSourceGeneration)
    {
        var invoice = Assert.IsType<NestedDefaultInvoice>(DeserializeEdgeCase<NestedDefaultItem>("$type: document\nkind: invoice\nTotal: 3\n", useSourceGeneration));

        Assert.Equal(3, invoice.Total);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadingAPolymorphicTypeWithoutDiscriminatorStillSelectsItsDefaultDerivedType(bool useSourceGeneration)
    {
        Assert.IsType<NestedDefaultReport>(DeserializeEdgeCase<NestedDefaultItem>("$type: document\nkind: unknown\nAuthor: a\n", useSourceGeneration));
        Assert.IsType<NestedDefaultReport>(DeserializeEdgeCase<NestedDefaultDocument>("Author: a\n", useSourceGeneration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAbstractDerivedTypeSelectedByItsDiscriminatorKeepsItsDefaultDerivedType(bool useSourceGeneration)
    {
        var square = Assert.IsType<NestedAbstractSquare>(DeserializeEdgeCase<NestedAbstractRoot>("$type: shape\nSide: 2\n", useSourceGeneration));

        Assert.Equal(2, square.Side);
    }

    [YamlDerivedType(typeof(DuplicateTypeDerived), "one")]
    [YamlDerivedType(typeof(DuplicateTypeDerived), "two")]
    private class DuplicateTypeBase
    {
    }

    private sealed class DuplicateTypeDerived : DuplicateTypeBase
    {
    }

    [YamlDerivedType(typeof(DuplicateDiscriminatorFirst), "same")]
    [YamlDerivedType(typeof(DuplicateDiscriminatorSecond), "same")]
    private class DuplicateDiscriminatorBase
    {
    }

    private sealed class DuplicateDiscriminatorFirst : DuplicateDiscriminatorBase
    {
    }

    private sealed class DuplicateDiscriminatorSecond : DuplicateDiscriminatorBase
    {
    }

    [YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag)]
    [YamlDerivedType(typeof(DuplicateTagFirst), Tag = "!same")]
    [YamlDerivedType(typeof(DuplicateTagSecond), Tag = "!same")]
    private class DuplicateTagBase
    {
    }

    private sealed class DuplicateTagFirst : DuplicateTagBase
    {
    }

    private sealed class DuplicateTagSecond : DuplicateTagBase
    {
    }

    [YamlDerivedType(typeof(DuplicateDefaultFirst))]
    [YamlDerivedType(typeof(DuplicateDefaultSecond))]
    private class DuplicateDefaultBase
    {
    }

    private sealed class DuplicateDefaultFirst : DuplicateDefaultBase
    {
    }

    private sealed class DuplicateDefaultSecond : DuplicateDefaultBase
    {
    }

    [Fact]
    public void RegisteringTheSameDerivedTypeTwiceThrows()
    {
        var writeException = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Serialize<DuplicateTypeBase>(new DuplicateTypeDerived()));
        var readException = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<DuplicateTypeBase>("$type: two\n"));

        Assert.Contains("registers the derived type", writeException.Message);
        Assert.IsType<InvalidOperationException>(readException.InnerException);
    }

    [Fact]
    public void RegisteringTheSameDiscriminatorTwiceThrows()
    {
        var writeException = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Serialize<DuplicateDiscriminatorBase>(new DuplicateDiscriminatorSecond()));
        var readException = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<DuplicateDiscriminatorBase>("$type: same\n"));

        Assert.Contains("registers the discriminator 'same'", writeException.Message);
        Assert.IsType<InvalidOperationException>(readException.InnerException);
    }

    [Fact]
    public void RegisteringTheSameTagTwiceThrows()
    {
        var readException = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<DuplicateTagBase>("!same\n{}\n"));

        Assert.Contains("registers the tag '!same'", readException.InnerException!.Message);
    }

    [Fact]
    public void RegisteringTwoDefaultDerivedTypesThrows()
    {
        var writeException = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Serialize<DuplicateDefaultBase>(new DuplicateDefaultFirst()));

        Assert.Contains("as its default derived type", writeException.Message);
    }

    private static string SerializeEdgeCase<T>(T value, bool useSourceGeneration, YamlSerializerOptions? options = null)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, typeof(T), new PolymorphismEdgeCaseYamlContext(options ?? new YamlSerializerOptions()))
            : YamlSerializer.Serialize(value, typeof(T), options);

    private static T? DeserializeEdgeCase<T>(string yaml, bool useSourceGeneration, YamlSerializerOptions? options = null)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, new PolymorphismEdgeCaseYamlContext(options ?? new YamlSerializerOptions()))
            : YamlSerializer.Deserialize<T>(yaml, options);

    private static string Serialize(SubclassPet value, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Serialize(value, typeof(SubclassPet), new SubclassPetYamlContext())
            : YamlSerializer.Serialize(value, typeof(SubclassPet));
}
