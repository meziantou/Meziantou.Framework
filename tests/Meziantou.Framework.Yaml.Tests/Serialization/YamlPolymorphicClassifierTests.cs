#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

[YamlPolymorphic]
[YamlDerivedType(typeof(ClassifiedCircle), "circle")]
[YamlDerivedType(typeof(ClassifiedSquare), "square")]
internal abstract class ClassifiedShape
{
}

internal sealed class ClassifiedCircle : ClassifiedShape
{
    public int Radius { get; set; }
}

internal sealed class ClassifiedSquare : ClassifiedShape
{
    public int Size { get; set; }
}

[YamlSerializable(typeof(ClassifiedShape))]
[YamlSerializable(typeof(ClassifiedCircle))]
[YamlSerializable(typeof(ClassifiedSquare))]
internal sealed partial class ClassifiedShapeYamlContext : YamlSerializerContext
{
    public ClassifiedShapeYamlContext()
    {
    }

    public ClassifiedShapeYamlContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}

/// <summary>Selects a derived type from the first key of the mapping.</summary>
internal sealed class ShapeByFirstKeyClassifier : YamlTypeClassifierFactory
{
    public static YamlTypeClassifierContext? LastContext { get; private set; }

    public override bool CanClassify(YamlTypeClassifierContext context)
        => context.Kind is YamlTypeClassifierKind.PolymorphicType && context.DeclaringType == typeof(ClassifiedShape);

    public override YamlTypeClassifier CreateYamlClassifier(YamlTypeClassifierContext context, YamlSerializerOptions options)
    {
        LastContext = context;
        return Classify;
    }

    private static Type? Classify(YamlReader reader)
    {
        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            return null;
        }

        reader.Read();
        return reader.TokenType == YamlTokenType.Scalar
            ? reader.ScalarValue switch
            {
                "Radius" => typeof(ClassifiedCircle),
                "Size" => typeof(ClassifiedSquare),
                _ => null,
            }
            : null;
    }
}

public sealed class YamlPolymorphicClassifierTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassifierSelectsDerivedTypeWithoutDiscriminator(bool useSourceGeneration)
    {
        var circle = Deserialize("Radius: 3\n", useSourceGeneration, ClassifierOptions);
        var square = Deserialize("Size: 4\n", useSourceGeneration, ClassifierOptions);

        Assert.Equal(3, Assert.IsType<ClassifiedCircle>(circle).Radius);
        Assert.Equal(4, Assert.IsType<ClassifiedSquare>(square).Size);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscriminatorTakesPrecedenceOverTheClassifier(bool useSourceGeneration)
    {
        var value = Deserialize("$type: square\nRadius: 3\n", useSourceGeneration, ClassifierOptions);

        Assert.IsType<ClassifiedSquare>(value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassifierIsNotUsedWhenNotRegistered(bool useSourceGeneration)
    {
        Assert.Throws<YamlException>(() => Deserialize("Radius: 3\n", useSourceGeneration, new YamlSerializerOptions()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassifierIsNotUsedWhenTheValueCannotBeClassified(bool useSourceGeneration)
    {
        Assert.Throws<YamlException>(() => Deserialize("Depth: 3\n", useSourceGeneration, ClassifierOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextDescribesTheRegisteredDerivedTypes(bool useSourceGeneration)
    {
        _ = Deserialize("Radius: 3\n", useSourceGeneration, ClassifierOptions);

        var context = ShapeByFirstKeyClassifier.LastContext;
        Assert.NotNull(context);
        Assert.Equal(YamlTypeClassifierKind.PolymorphicType, context.Kind);
        Assert.Equal(typeof(ClassifiedShape), context.DeclaringType);
        Assert.Equal("$type", context.TypeDiscriminatorPropertyName);
        Assert.Equal(
            ["circle", "square"],
            context.DerivedTypes.Select(d => d.Discriminator).Order(StringComparer.Ordinal));
    }

    private static YamlSerializerOptions ClassifierOptions { get; } = new() { TypeClassifiers = [new ShapeByFirstKeyClassifier()] };

    private static ClassifiedShape? Deserialize(string yaml, bool useSourceGeneration, YamlSerializerOptions options)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<ClassifiedShape>(yaml, new ClassifiedShapeYamlContext(options))
            : YamlSerializer.Deserialize<ClassifiedShape>(yaml, options);
}
