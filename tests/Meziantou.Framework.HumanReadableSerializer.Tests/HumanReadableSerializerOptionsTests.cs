namespace Meziantou.Framework.HumanReadable.Tests;
public sealed class HumanReadableSerializerOptionsTests
{
    [Fact]
    public void CloneShouldCreateNewConvertersInstance()
    {
        var options = new HumanReadableSerializerOptions();
        options.Converters.Add(new DummyConverter());
        HumanReadableSerializer.Serialize(12, options);

        var clone = options with { };

        Assert.Single(clone.Converters);
        clone.Converters.Clear();
        HumanReadableSerializer.Serialize(12, clone);

        Assert.Empty(clone.Converters);
        Assert.Single(options.Converters);
    }

    [Fact]
    public void CloneShouldCopyAllSettings()
    {
        var options = new HumanReadableSerializerOptions
        {
            DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull,
            DictionaryKeyOrder = StringComparer.Ordinal,
            IncludeFields = true,
            IncludeObsoleteMembers = true,
            MaxDepth = 12,
            NewLine = "\r\n",
            PropertyOrder = StringComparer.OrdinalIgnoreCase,
            ShowInvisibleCharactersInValues = true,
        };

        var clone = options with { };

        Assert.Equal(HumanReadableIgnoreCondition.WhenWritingNull, clone.DefaultIgnoreCondition);
        Assert.Equal(StringComparer.Ordinal, clone.DictionaryKeyOrder);
        Assert.Equal(true, clone.IncludeFields);
        Assert.Equal(true, clone.IncludeObsoleteMembers);
        Assert.Equal(12, clone.MaxDepth);
        Assert.Equal("\r\n", clone.NewLine);
        Assert.Equal(StringComparer.OrdinalIgnoreCase, clone.PropertyOrder);
        Assert.Equal(true, clone.ShowInvisibleCharactersInValues);
    }

    [Fact]
    public void ReadOnly_SettingsCannotBeChanged()
    {
        var options = new HumanReadableSerializerOptions();
        options.MakeReadOnly();

        Assert.Throws<InvalidOperationException>(() => options.MaxDepth = 1);
        Assert.Throws<InvalidOperationException>(() => options.ShowInvisibleCharactersInValues = true);
        Assert.Throws<InvalidOperationException>(() => options.IncludeFields = true);
    }

    [Fact]
    public void NewLine_DefaultsToLineFeed()
    {
        var text = HumanReadableSerializer.Serialize(new MultiLinePayload { Value = "line1\r\nline2" });

        Assert.Equal("\n", new HumanReadableSerializerOptions().NewLine);
        Assert.Equal("A: 1\nValue:\n  line1\n  line2", text);
    }

    [Fact]
    public void NewLine_CarriageReturnLineFeed()
    {
        var options = new HumanReadableSerializerOptions { NewLine = "\r\n" };

        var text = HumanReadableSerializer.Serialize(new MultiLinePayload { Value = "line1\nline2" }, options);

        Assert.Equal("A: 1\r\nValue:\r\n  line1\r\n  line2", text);
    }

    [Theory]
    [InlineData("\n", "a\u240D\u240A\nb\u240D\nc\u240A\nd")]
    [InlineData("\r\n", "a\u240D\u240A\r\nb\u240D\r\nc\u240A\r\nd")]
    public void NewLine_ShowInvisibleCharacters(string newLine, string expected)
    {
        var options = new HumanReadableSerializerOptions { NewLine = newLine, ShowInvisibleCharactersInValues = true };

        var text = HumanReadableSerializer.Serialize("a\r\nb\rc\nd", options);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r")]
    [InlineData(" ")]
    public void NewLine_RejectsUnsupportedValues(string newLine)
    {
        var options = new HumanReadableSerializerOptions();

        Assert.Throws<ArgumentException>(() => options.NewLine = newLine);
    }

    [Fact]
    public void NewLine_IsCopiedByClone()
    {
        var options = new HumanReadableSerializerOptions { NewLine = "\r\n" };

        var clone = options with { };

        Assert.Equal("\r\n", clone.NewLine);
    }

    [Fact]
    public void DefaultOptionsProduceTheSameOutputAsAnExplicitInstance()
    {
        var subject = new Payload();

        Assert.Equal(HumanReadableSerializer.Serialize(subject, new HumanReadableSerializerOptions()), HumanReadableSerializer.Serialize(subject));
    }

    [Fact]
    public void DefaultOptionsCanBeUsedConcurrently()
    {
        var subject = new Payload();
        var expected = HumanReadableSerializer.Serialize(subject);

        var results = new string[64];
        Parallel.For(0, results.Length, i => results[i] = HumanReadableSerializer.Serialize(subject));

        Assert.All(results, result => Assert.Equal(expected, result));
    }

    // Deliberately not an anonymous type: SerializerTests.Type_AnonymType asserts a
    // Roslyn-generated anonymous type ordinal, which shifts when new shapes are added.
    private sealed class Payload
    {
        public int Id { get; } = 1;
        public string Name { get; } = "test";
        public string[] Tags { get; } = ["a", "b"];
        public DateTime When { get; } = new(2123, 4, 5, 6, 7, 8, DateTimeKind.Utc);
    }

    private sealed class MultiLinePayload
    {
        public int A { get; } = 1;
        public string? Value { get; init; }
    }

    private sealed class DummyConverter : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => false;
        public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options) { }
    }
}
