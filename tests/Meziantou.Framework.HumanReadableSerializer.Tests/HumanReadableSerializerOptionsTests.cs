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

    private sealed class DummyConverter : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => false;
        public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options) { }
    }
}
