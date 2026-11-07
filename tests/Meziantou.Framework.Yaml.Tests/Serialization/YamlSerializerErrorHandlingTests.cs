#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;

internal sealed class ErrorHandlingNonNullableObject
{
    public object Obj { get; set; } = "";
}

internal sealed class ErrorHandlingNonNullableInitObject
{
    public object Obj { get; init; } = "";
}

internal sealed class ErrorHandlingDecimalHolder
{
    public decimal Value { get; set; }
}

internal sealed record ErrorHandlingDecimalRecord(decimal Value);

internal sealed class ErrorHandlingAliasSource
{
    public string? Name { get; set; }
}

internal sealed class ErrorHandlingAliasTarget
{
    public string? Name { get; set; }
}

internal sealed class ErrorHandlingPopulateHolder
{
    public ErrorHandlingAliasSource? A { get; set; }

    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    public ErrorHandlingAliasTarget B { get; set; } = new();
}

internal sealed class ErrorHandlingAliasHolder
{
    public ErrorHandlingAliasSource? A { get; set; }

    public ErrorHandlingAliasSource? B { get; set; }

    public List<int>? C { get; set; }

    public List<int>? D { get; set; }

    public int E { get; set; }

    public int F { get; set; }
}

internal sealed class ErrorHandlingThrowingConverter : YamlConverter<int>
{
    public override int Read(YamlReader reader) => throw new FormatException("Invalid value");

    public override void Write(YamlWriter writer, int value) => writer.WriteScalar(value);
}

internal sealed class ErrorHandlingConverterHolder
{
    [YamlConverter(typeof(ErrorHandlingThrowingConverter))]
    public int Value { get; set; }
}

internal sealed class ErrorHandlingConverterInitHolder
{
    [YamlConverter(typeof(ErrorHandlingThrowingConverter))]
    public int Value { get; init; }
}

internal abstract class ErrorHandlingAbstractModel
{
    public string? Name { get; set; }
}

internal sealed class ErrorHandlingAbstractHolder
{
    public ErrorHandlingAbstractModel? Value { get; set; }
}

[YamlPolymorphic]
[YamlDerivedType(typeof(ErrorHandlingCircle), "circle")]
internal interface IErrorHandlingShape
{
}

internal sealed class ErrorHandlingCircle : IErrorHandlingShape
{
    public int Radius { get; set; }
}

internal sealed class ErrorHandlingShapeHolder
{
    public IErrorHandlingShape? A { get; set; }

    public IErrorHandlingShape? B { get; set; }
}

[YamlSerializable(typeof(ErrorHandlingNonNullableObject))]
[YamlSerializable(typeof(ErrorHandlingNonNullableInitObject))]
[YamlSerializable(typeof(ErrorHandlingDecimalHolder))]
[YamlSerializable(typeof(ErrorHandlingDecimalRecord))]
[YamlSerializable(typeof(ErrorHandlingPopulateHolder))]
[YamlSerializable(typeof(ErrorHandlingAliasHolder))]
[YamlSerializable(typeof(ErrorHandlingConverterHolder))]
[YamlSerializable(typeof(ErrorHandlingConverterInitHolder))]
[YamlSerializable(typeof(ErrorHandlingAbstractHolder))]
[YamlSerializable(typeof(ErrorHandlingShapeHolder))]
internal sealed partial class ErrorHandlingYamlContext : YamlSerializerContext
{
    public ErrorHandlingYamlContext()
    {
    }

    public ErrorHandlingYamlContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}

public class YamlSerializerErrorHandlingTests
{
    [Fact]
    public void Reflection_ThrowsOnIntegerOverflow_WithLocation()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<int>("999999999999999999999"));
        Assert.Contains("Lin:", ex.Message);
        Assert.Contains("Col:", ex.Message);
    }

    [Fact]
    public void Reflection_ThrowsOnTypeMismatch_MappingToScalar()
    {
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<int>("a: 1"));
    }

    [Fact]
    public void Reflection_ThrowsOnTypeMismatch_ScalarToMapping()
    {
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, int>>("1"));
    }

    [Fact]
    public void Reflection_ThrowsOnTypeMismatch_MappingToSequence()
    {
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<List<int>>("a: 1"));
    }

    [Fact]
    public void SourceGen_ThrowsOnIntegerOverflow_WithLocation()
    {
        var context = TestYamlSerializerContext.Default;
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("999999999999999999999", context.Int32));
        Assert.Contains("Lin:", ex.Message);
        Assert.Contains("Col:", ex.Message);
    }

    [Fact]
    public void SourceGen_ThrowsOnTypeMismatch_MappingToScalar()
    {
        var context = TestYamlSerializerContext.Default;
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("a: 1", context.Int32));
    }

    [Fact]
    public void SourceGen_ThrowsOnTypeMismatch_ScalarToMapping()
    {
        var context = TestYamlSerializerContext.Default;
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("1", context.DictionaryStringInt32));
    }

    [Fact]
    public void SourceGen_ThrowsOnTypeMismatch_MappingToSequence()
    {
        var context = TestYamlSerializerContext.Default;
        _ = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("a: 1", context.ListInt32));
    }

    [Fact]
    public void SourceGen_PropagatesSourceName()
    {
        var context = new TestYamlSerializerContext(new YamlSerializerOptions { SourceName = "input.yml" });
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize("a: 1", context.Int32));
        Assert.Equal("input.yml", ex.SourceName);
    }

    [Theory]
    [InlineData(false, "Obj: ~\n", false)]
    [InlineData(true, "Obj: ~\n", false)]
    [InlineData(false, "Obj:\n", false)]
    [InlineData(true, "Obj:\n", false)]
    [InlineData(false, "Obj: !!null \"\"\n", true)]
    [InlineData(true, "Obj: !!null \"\"\n", true)]
    public void NonNullableObjectMember_RejectsNull(bool useSourceGeneration, string yaml, bool useSchema)
    {
        var options = new YamlSerializerOptions { UseSchema = useSchema };

        var exception = Assert.Throws<YamlException>(() => Deserialize<ErrorHandlingNonNullableObject>(yaml, useSourceGeneration, options));
        Assert.Contains("non-nullable", exception.Message, StringComparison.Ordinal);

        exception = Assert.Throws<YamlException>(() => Deserialize<ErrorHandlingNonNullableInitObject>(yaml, useSourceGeneration, options));
        Assert.Contains("non-nullable", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonNullableObjectMember_AcceptsNullWhenNullableAnnotationsAreNotRespected(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { RespectNullableAnnotations = false };

        var result = Deserialize<ErrorHandlingNonNullableObject>("Obj: ~\n", useSourceGeneration, options);
        var initResult = Deserialize<ErrorHandlingNonNullableInitObject>("Obj: ~\n", useSourceGeneration, options);

        Assert.NotNull(result);
        Assert.Null(result.Obj);
        Assert.NotNull(initResult);
        Assert.Null(initResult.Obj);
    }

    [Theory]
    [InlineData(false, "1e40")]
    [InlineData(true, "1e40")]
    [InlineData(false, "123456789012345678901234567890123456789")]
    [InlineData(true, "123456789012345678901234567890123456789")]
    public void DecimalMemberOverflow_IsReportedAtTheMemberKey(bool useSourceGeneration, string value)
    {
        var options = new YamlSerializerOptions { UseSchema = true };

        var exception = Assert.Throws<YamlException>(() => Deserialize<ErrorHandlingDecimalHolder>("x: 1\nValue: " + value + "\n", useSourceGeneration, options));
        AssertReportedAtKey<OverflowException>(exception, line: 1, startColumn: 0, endColumn: 5);

        exception = Assert.Throws<YamlException>(() => Deserialize<ErrorHandlingDecimalRecord>("x: 1\nValue: " + value + "\n", useSourceGeneration, options));
        AssertReportedAtKey<OverflowException>(exception, line: 1, startColumn: 0, endColumn: 5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopulatedMemberAliasOfAnotherType_IsReportedAtTheMemberKey(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

        var exception = Assert.Throws<YamlException>(() => Deserialize<ErrorHandlingPopulateHolder>("A: &a {Name: x}\nB: *a\n", useSourceGeneration, options));

        Assert.NotNull(exception.InnerException);
        Assert.Equal(1, exception.Start.Line);
        Assert.Equal(0, exception.Start.Column);
        Assert.Equal(1, exception.End.Line);
        Assert.Equal(1, exception.End.Column);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConverterException_IsReportedAtTheMemberKey(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<ErrorHandlingConverterHolder>("x: 1\nValue: 1\n", useSourceGeneration, new YamlSerializerOptions()));
        AssertReportedAtKey<FormatException>(exception, line: 1, startColumn: 0, endColumn: 5);
        Assert.Contains("Invalid value", exception.Message, StringComparison.Ordinal);

        exception = Assert.Throws<YamlException>(() => Deserialize<ErrorHandlingConverterInitHolder>("x: 1\nValue: 1\n", useSourceGeneration, new YamlSerializerOptions()));
        AssertReportedAtKey<FormatException>(exception, line: 1, startColumn: 0, endColumn: 5);
        Assert.Contains("Invalid value", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "A: &a {Name: x}\nB: *a\n")]
    [InlineData(true, "A: &a {Name: x}\nB: *a\n")]
    [InlineData(false, "C: &c [1]\nD: *c\n")]
    [InlineData(true, "C: &c [1]\nD: *c\n")]
    public void AliasWithoutReferenceHandling_IsReportedAsUnsupported(bool useSourceGeneration, string yaml)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<ErrorHandlingAliasHolder>(yaml, useSourceGeneration, new YamlSerializerOptions()));

        Assert.Contains("Aliases are not supported", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unless ReferenceHandling is Preserve", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, exception.Start.Line);
        Assert.Equal(3, exception.Start.Column);
    }

    [Theory]
    [InlineData("A: &a {Name: x}\nB: *a\n", "Aliases are not supported when deserializing into 'Meziantou.Framework.Yaml.Tests.Serialization.ErrorHandlingAliasSource' unless ReferenceHandling is Preserve.")]
    [InlineData("*a\n", "Aliases are not supported when deserializing into 'Meziantou.Framework.Yaml.Tests.Serialization.ErrorHandlingAliasHolder' unless ReferenceHandling is Preserve.")]
    [InlineData("A: {Name: &n x}\nB: {Name: *n}\n", "Aliases are not supported when deserializing into string unless ReferenceHandling is Preserve.")]
    [InlineData("A: &a {Name: x}\nB: {<<: *a}\n", "Aliases are not supported when deserializing into 'Meziantou.Framework.Yaml.Tests.Serialization.ErrorHandlingAliasSource' unless ReferenceHandling is Preserve.")]
    public void AliasWithoutReferenceHandling_ReportsTheSameMessageInBothModes(string yaml, string expectedMessage)
    {
        AssertSameMessageInBothModes<ErrorHandlingAliasHolder>(yaml, new YamlSerializerOptions { Schema = YamlSchemaKind.Core }, expectedMessage);
    }

    [Fact]
    public void InterfaceAliasWithoutReferenceHandling_ReportsTheSameMessageInBothModes()
    {
        AssertSameMessageInBothModes<ErrorHandlingShapeHolder>(
            "A: &a {$type: circle, Radius: 1}\nB: *a\n",
            new YamlSerializerOptions(),
            "Aliases are not supported when deserializing into 'Meziantou.Framework.Yaml.Tests.Serialization.IErrorHandlingShape' unless ReferenceHandling is Preserve.");
    }

    [Theory]
    [InlineData("A: {Name: *zz}\n")]
    [InlineData("F: *zz\n")]
    [InlineData("B: *zz\n")]
    [InlineData("D: *zz\n")]
    public void UnknownAliasWithReferenceHandling_IsReportedAsUnknownInBothModes(string yaml)
    {
        AssertSameMessageInBothModes<ErrorHandlingAliasHolder>(yaml, new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve }, "Unknown YAML alias '*zz'.");
    }

    [Fact]
    public void AliasToMappingReadAsString_ReportsTheSameMessageInBothModes()
    {
        AssertSameMessageInBothModes<ErrorHandlingAliasHolder>(
            "A: &a {Name: x}\nB: {Name: *a}\n",
            new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve },
            "Expected a Scalar token but found 'Alias'.");
    }

    [Fact]
    public void AbstractTypeWithoutPolymorphism_ReportsTheSameMessageInBothModes()
    {
        AssertSameMessageInBothModes<ErrorHandlingAbstractHolder>(
            "Value: {Name: x}\n",
            new YamlSerializerOptions(),
            "Type 'Meziantou.Framework.Yaml.Tests.Serialization.ErrorHandlingAbstractModel' cannot be instantiated.");
    }

    private static void AssertSameMessageInBothModes<T>(string yaml, YamlSerializerOptions options, string expectedMessage)
    {
        var reflectionException = Assert.Throws<YamlException>(() => Deserialize<T>(yaml, useSourceGeneration: false, options));
        var generatedException = Assert.Throws<YamlException>(() => Deserialize<T>(yaml, useSourceGeneration: true, options));

        Assert.EndsWith("): " + expectedMessage, reflectionException.Message);
        Assert.Equal(reflectionException.Message, generatedException.Message);
    }

    private static void AssertReportedAtKey<TInnerException>(YamlException exception, int line, int startColumn, int endColumn)
        where TInnerException : Exception
    {
        _ = Assert.IsType<TInnerException>(exception.InnerException);
        Assert.Equal(line, exception.Start.Line);
        Assert.Equal(startColumn, exception.Start.Column);
        Assert.Equal(line, exception.End.Line);
        Assert.Equal(endColumn, exception.End.Column);
    }

    private static T? Deserialize<T>(string yaml, bool useSourceGeneration, YamlSerializerOptions options)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, new ErrorHandlingYamlContext(options))
            : YamlSerializer.Deserialize<T>(yaml, options);
}

