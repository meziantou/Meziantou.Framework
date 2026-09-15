using System.Reflection;

namespace Meziantou.Framework.HumanReadable.Tests;
public sealed class HumanReadableSerializerOptionsTests
{
    [Fact]
    public void CloneShouldCopyAllSettableProperties()
    {
        var properties = GetSettableProperties();
        Assert.NotEmpty(properties);

        var defaults = new HumanReadableSerializerOptions();
        var options = new HumanReadableSerializerOptions();
        foreach (var property in properties)
        {
            property.SetValue(options, CreateNonDefaultValue(property, property.GetValue(defaults)));
        }

        var clone = options with { };

        foreach (var property in properties)
        {
            Assert.Equal(property.GetValue(options), property.GetValue(clone), message: property.Name);
        }
    }

    [Fact]
    public void CloneOfDefaultOptionsShouldKeepDefaultValues()
    {
        var defaults = new HumanReadableSerializerOptions();

        var clone = defaults with { };

        Assert.Equal(64, clone.MaxDepth);
        foreach (var property in GetSettableProperties())
        {
            Assert.Equal(property.GetValue(defaults), property.GetValue(clone), message: property.Name);
        }
    }

    [Fact]
    public void CloneOfReadOnlyOptionsShouldBeMutableAndKeepSettings()
    {
        var options = new HumanReadableSerializerOptions
        {
            PropertyOrder = StringComparer.Ordinal,
            DictionaryKeyOrder = StringComparer.OrdinalIgnoreCase,
            IncludeObsoleteMembers = true,
        };
        options.MakeReadOnly();

        var clone = options with { };
        clone.IncludeFields = true;

        Assert.False(clone.IsReadOnly);
        Assert.Same(StringComparer.Ordinal, clone.PropertyOrder);
        Assert.Same(StringComparer.OrdinalIgnoreCase, clone.DictionaryKeyOrder);
        Assert.True(clone.IncludeObsoleteMembers);
    }

    private static PropertyInfo[] GetSettableProperties()
    {
        return typeof(HumanReadableSerializerOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod is { IsPublic: true })
            .ToArray();
    }

    private static object CreateNonDefaultValue(PropertyInfo property, object? defaultValue)
    {
        var type = property.PropertyType;
        object value = type switch
        {
            _ when type == typeof(bool) => !(bool)defaultValue!,
            _ when type == typeof(int) => (int)defaultValue! + 1,
            _ when type.IsEnum => Enum.GetValues(type).Cast<object>().First(item => !item.Equals(defaultValue)),
            _ when type == typeof(IComparer<string>) => StringComparer.Ordinal,
            _ => throw new InvalidOperationException($"Cannot create a non-default value for property '{property.Name}' of type '{type}'. Update the test to support this type."),
        };

        Assert.NotEqual(defaultValue, value);
        return value;
    }

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
