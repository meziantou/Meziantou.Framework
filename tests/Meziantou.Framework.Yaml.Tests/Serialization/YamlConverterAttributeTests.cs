using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlConverterAttributeTests
{
    [Fact]
    public void Deserialize_UsesPropertyLevelConverter()
    {
        var value = YamlSerializer.Deserialize<PropertyLevelModel>("A: 1\n")!;
        Assert.Equal(2, value.A);
    }

    [Fact]
    public void Serialize_UsesPropertyLevelConverter()
    {
        var yaml = YamlSerializer.Serialize(new PropertyLevelModel { A = 1 });
        Assert.Contains("A: 2", yaml);
    }

    [Fact]
    public void Serialize_UsesTypeLevelConverter()
    {
        var yaml = YamlSerializer.Serialize(new TypeLevelContainer { Value = new CustomScalar { Text = "hello" } });

        Assert.Contains("Value: hello", yaml);
        Assert.DoesNotContain("Text:", yaml);
    }

    [Fact]
    public void Deserialize_UsesTypeLevelConverter()
    {
        var value = YamlSerializer.Deserialize<TypeLevelContainer>("Value: hello\n")!;
        Assert.NotNull(value.Value);
        Assert.Equal("hello", value.Value!.Text);
    }

    private sealed class PropertyLevelModel
    {
        [YamlConverter(typeof(IncrementIntConverter))]
        public int A { get; set; }
    }

    private sealed class IncrementIntConverter : YamlConverter<int>
    {
        public override int Read(YamlReader reader)
        {
            var scalar = reader.GetScalarValue();
            reader.Read();
            return int.Parse(scalar, CultureInfo.InvariantCulture) + 1;
        }

        public override void Write(YamlWriter writer, int value)
        {
            writer.WriteScalar((value + 1).ToString(CultureInfo.InvariantCulture));
        }
    }

    private sealed class TypeLevelContainer
    {
        public CustomScalar? Value { get; set; }
    }

    [YamlConverter(typeof(CustomScalarConverter))]
    private sealed class CustomScalar
    {
        public string? Text { get; set; }
    }

    private sealed class CustomScalarConverter : YamlConverter<CustomScalar?>
    {
        public override CustomScalar? Read(YamlReader reader)
        {
            if (reader.TokenType is YamlTokenType.Scalar && YamlScalar.IsNull(reader.ScalarValue.AsSpan()))
            {
                reader.Read();
                return null;
            }

            var scalar = reader.GetScalarValue();
            reader.Read();
            return new CustomScalar { Text = scalar };
        }

        public override void Write(YamlWriter writer, CustomScalar? value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteScalar(value.Text);
        }
    }

    [Fact]
    public void Serialize_UsesOpenGenericTypeLevelConverter()
    {
        var yaml = YamlSerializer.Serialize(new BoxContainer { Number = new Box<int>(42), Text = new Box<string>("hello") });

        Assert.Contains("Number: 42", yaml);
        Assert.Contains("Text: hello", yaml);
    }

    [Fact]
    public void Deserialize_UsesOpenGenericTypeLevelConverter()
    {
        var value = YamlSerializer.Deserialize<BoxContainer>("Number: 42\nText: hello\n")!;

        Assert.Equal(42, value.Number!.Value);
        Assert.Equal("hello", value.Text!.Value);
    }

    [Fact]
    public void RoundTrip_UsesOpenGenericConverterAtRoot()
    {
        var yaml = YamlSerializer.Serialize(new Box<int>(1));
        var value = YamlSerializer.Deserialize<Box<int>>(yaml)!;

        Assert.Equal(1, value.Value);
    }

    [Fact]
    public void RoundTrip_UsesOpenGenericPropertyConverterWithMultipleTypeParameters()
    {
        var yaml = YamlSerializer.Serialize(new PairModel { Pair = new Pair<int, string> { First = 1, Second = "two" } });
        Assert.Contains("Pair: 1|two", yaml);

        var value = YamlSerializer.Deserialize<PairModel>(yaml)!;
        Assert.Equal(1, value.Pair!.First);
        Assert.Equal("two", value.Pair.Second);
    }

    [Fact]
    public void RoundTrip_UsesNestedOpenGenericPropertyConverter()
    {
        var yaml = YamlSerializer.Serialize(new NestedConverterPairModel { Pair = new Pair<int, string> { First = 3, Second = "four" } });
        Assert.Contains("Pair: 3|four", yaml);

        var value = YamlSerializer.Deserialize<NestedConverterPairModel>(yaml)!;
        Assert.Equal(3, value.Pair!.First);
        Assert.Equal("four", value.Pair.Second);
    }

    [Fact]
    public void Serialize_OpenGenericConverterOnNonGenericType_Throws()
    {
        var exception = Assert.Throws<NotSupportedException>(() => YamlSerializer.Serialize(new NonGenericWithOpenGenericConverter()));

        Assert.Contains("open generic converter type", exception.Message);
    }

    [Fact]
    public void Serialize_OpenGenericConverterWithMismatchedArity_Throws()
    {
        var exception = Assert.Throws<NotSupportedException>(() => YamlSerializer.Serialize(new ArityMismatchModel { Value = new Cell<int>() }));

        Assert.Contains("open generic converter type", exception.Message);
    }

    [Fact]
    public void Serialize_OpenGenericConverterWithUnsatisfiedConstraint_Throws()
    {
        var exception = Assert.Throws<NotSupportedException>(() => YamlSerializer.Serialize(new ConstraintViolationModel { Value = new Cell<int>() }));

        Assert.Contains("generic constraints are not satisfied", exception.Message);
    }

    private sealed class BoxContainer
    {
        public Box<int>? Number { get; set; }

        public Box<string>? Text { get; set; }
    }

    [YamlConverter(typeof(BoxConverter<>))]
    private sealed class Box<T>
    {
        public Box(T? value) => Value = value;

        public T? Value { get; }
    }

    private sealed class BoxConverter<T> : YamlConverter<Box<T>?>
    {
        public override Box<T>? Read(YamlReader reader)
        {
            if (reader.TokenType is YamlTokenType.Scalar && YamlScalar.IsNull(reader.ScalarValue.AsSpan()))
            {
                reader.Read();
                return null;
            }

            var scalar = reader.GetScalarValue();
            reader.Read();
            return new Box<T>((T)Convert.ChangeType(scalar, typeof(T), CultureInfo.InvariantCulture));
        }

        public override void Write(YamlWriter writer, Box<T>? value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteScalar(Convert.ToString(value.Value, CultureInfo.InvariantCulture));
        }
    }

    private sealed class Pair<TFirst, TSecond>
    {
        public TFirst? First { get; set; }

        public TSecond? Second { get; set; }
    }

    private sealed class PairModel
    {
        [YamlConverter(typeof(PairConverter<,>))]
        public Pair<int, string>? Pair { get; set; }
    }

    private sealed class NestedConverterPairModel
    {
        [YamlConverter(typeof(ConverterHost<>.NestedPairConverter<>))]
        public Pair<int, string>? Pair { get; set; }
    }

    private sealed class PairConverter<TFirst, TSecond> : YamlConverter<Pair<TFirst, TSecond>?>
    {
        public override Pair<TFirst, TSecond>? Read(YamlReader reader) => ReadPair<TFirst, TSecond>(reader);

        public override void Write(YamlWriter writer, Pair<TFirst, TSecond>? value) => WritePair(writer, value);
    }

    private sealed class ConverterHost<TFirst>
    {
        internal sealed class NestedPairConverter<TSecond> : YamlConverter<Pair<TFirst, TSecond>?>
        {
            public override Pair<TFirst, TSecond>? Read(YamlReader reader) => ReadPair<TFirst, TSecond>(reader);

            public override void Write(YamlWriter writer, Pair<TFirst, TSecond>? value) => WritePair(writer, value);
        }
    }

    private static Pair<TFirst, TSecond>? ReadPair<TFirst, TSecond>(YamlReader reader)
    {
        if (reader.TokenType is YamlTokenType.Scalar && YamlScalar.IsNull(reader.ScalarValue.AsSpan()))
        {
            reader.Read();
            return null;
        }

        var scalar = reader.GetScalarValue();
        reader.Read();

        var parts = scalar.Split('|');
        return new Pair<TFirst, TSecond>
        {
            First = (TFirst)Convert.ChangeType(parts[0], typeof(TFirst), CultureInfo.InvariantCulture),
            Second = (TSecond)Convert.ChangeType(parts[1], typeof(TSecond), CultureInfo.InvariantCulture),
        };
    }

    private static void WritePair<TFirst, TSecond>(YamlWriter writer, Pair<TFirst, TSecond>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteScalar(Convert.ToString(value.First, CultureInfo.InvariantCulture) + "|" + Convert.ToString(value.Second, CultureInfo.InvariantCulture));
    }

    [YamlConverter(typeof(BoxConverter<>))]
    private sealed class NonGenericWithOpenGenericConverter
    {
        public int Value { get; set; }
    }

    private sealed class Cell<T>
    {
        public T? Value { get; set; }
    }

    private sealed class ArityMismatchModel
    {
        [YamlConverter(typeof(PairConverter<,>))]
        public Cell<int>? Value { get; set; }
    }

    private sealed class ConstraintViolationModel
    {
        [YamlConverter(typeof(ReferenceCellConverter<>))]
        public Cell<int>? Value { get; set; }
    }

    private sealed class ReferenceCellConverter<T> : YamlConverter<Cell<T>?>
        where T : class
    {
        public override Cell<T>? Read(YamlReader reader) => throw new NotSupportedException();

        public override void Write(YamlWriter writer, Cell<T>? value) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_UsesTypeLevelConverterOfTransitiveMemberTypes(bool useSourceGeneration)
    {
        var value = new TransitiveConvertedModel
        {
            Value = new TransitiveConvertedScalar { Text = "a" },
            List = [new TransitiveConvertedScalar { Text = "b" }, null],
            Array = [new TransitiveConvertedScalar { Text = "c" }],
            Map = new Dictionary<string, TransitiveConvertedScalar?>(StringComparer.Ordinal) { ["key"] = new TransitiveConvertedScalar { Text = "d" } },
        };

        var yaml = useSourceGeneration
            ? YamlSerializer.Serialize(value, ConverterAttributeYamlContext.Default)
            : YamlSerializer.Serialize(value);

        Assert.Equal("Value: converted-a\nList:\n  - converted-b\n  - null\nArray:\n  - converted-c\nMap:\n  key: converted-d\n", yaml);

        var roundTrip = useSourceGeneration
            ? YamlSerializer.Deserialize<TransitiveConvertedModel>(yaml, ConverterAttributeYamlContext.Default)
            : YamlSerializer.Deserialize<TransitiveConvertedModel>(yaml);

        Assert.NotNull(roundTrip);
        Assert.Equal("a", roundTrip.Value!.Text);
        Assert.Equal("b", roundTrip.List![0]!.Text);
        Assert.Null(roundTrip.List[1]);
        Assert.Equal("c", roundTrip.Array![0]!.Text);
        Assert.Equal("d", roundTrip.Map!["key"]!.Text);
    }

    internal sealed class TransitiveConvertedModel
    {
        public TransitiveConvertedScalar? Value { get; set; }

        public List<TransitiveConvertedScalar?>? List { get; set; }

        public TransitiveConvertedScalar?[]? Array { get; set; }

        public Dictionary<string, TransitiveConvertedScalar?>? Map { get; set; }
    }

    [YamlConverter(typeof(TransitiveConvertedScalarConverter))]
    internal sealed class TransitiveConvertedScalar
    {
        public string? Text { get; set; }
    }

    internal sealed class TransitiveConvertedScalarConverter : YamlConverter<TransitiveConvertedScalar?>
    {
        private const string Prefix = "converted-";

        public override TransitiveConvertedScalar? Read(YamlReader reader)
        {
            if (reader.TokenType is YamlTokenType.Scalar && YamlScalar.IsNull(reader.ScalarValue.AsSpan()))
            {
                reader.Read();
                return null;
            }

            var scalar = reader.GetScalarValue();
            reader.Read();
            return new TransitiveConvertedScalar { Text = scalar[Prefix.Length..] };
        }

        public override void Write(YamlWriter writer, TransitiveConvertedScalar? value)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteScalar(Prefix + value.Text);
        }
    }
}

#pragma warning disable MA0048 // File name must match type name
[YamlSerializable(typeof(YamlConverterAttributeTests.TransitiveConvertedModel))]
internal sealed partial class ConverterAttributeYamlContext : YamlSerializerContext
{
}
