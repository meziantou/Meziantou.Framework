using System.Reflection;
using Meziantou.Framework.HumanReadable.ValueFormatters;

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
            _ when property.Name is nameof(HumanReadableSerializerOptions.NewLine) => "\r\n",
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

    public static TheoryData<string> Mutators() => [.. MutatorActions.Keys];

    private static readonly Dictionary<string, Action<HumanReadableSerializerOptions>> MutatorActions = new(StringComparer.Ordinal)
    {
        [nameof(HumanReadableSerializerOptions.MaxDepth)] = options => options.MaxDepth = 1,
        [nameof(HumanReadableSerializerOptions.ShowInvisibleCharactersInValues)] = options => options.ShowInvisibleCharactersInValues = true,
        [nameof(HumanReadableSerializerOptions.NewLine)] = options => options.NewLine = "\r\n",
        [nameof(HumanReadableSerializerOptions.PropertyOrder)] = options => options.PropertyOrder = StringComparer.Ordinal,
        [nameof(HumanReadableSerializerOptions.DictionaryKeyOrder)] = options => options.DictionaryKeyOrder = StringComparer.Ordinal,
        [nameof(HumanReadableSerializerOptions.IncludeFields)] = options => options.IncludeFields = true,
        [nameof(HumanReadableSerializerOptions.IncludeObsoleteMembers)] = options => options.IncludeObsoleteMembers = true,
        [nameof(HumanReadableSerializerOptions.DefaultIgnoreCondition)] = options => options.DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull,
        ["Converters.Add"] = options => options.Converters.Add(new DummyConverter()),
        ["Converters.Insert"] = options => options.Converters.Insert(0, new DummyConverter()),
        ["Converters.Clear"] = options => options.Converters.Clear(),
        [nameof(HumanReadableSerializerOptions.AddFormatter)] = options => options.AddFormatter("text/plain", new Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatter()),
        ["AddAttribute(Type)"] = options => options.AddAttribute(typeof(Payload), new HumanReadableConverterAttribute(typeof(DummyConverter))),
        ["AddAttribute(Type, string)"] = options => options.AddAttribute(typeof(Payload), nameof(Payload.Id), new HumanReadableIgnoreAttribute()),
        ["AddAttribute(PropertyInfo)"] = options => options.AddAttribute(typeof(Payload).GetProperty(nameof(Payload.Id))!, new HumanReadableIgnoreAttribute()),
        ["AddAttribute(Expression)"] = options => options.AddAttribute<Payload>(payload => payload.Id, new HumanReadableIgnoreAttribute()),
        [nameof(HumanReadableSerializerOptions.AddPropertyAttribute)] = options => options.AddPropertyAttribute(property => true, new HumanReadableIgnoreAttribute()),
        [nameof(HumanReadableSerializerOptions.AddFieldAttribute)] = options => options.AddFieldAttribute(field => true, new HumanReadableIgnoreAttribute()),
        [nameof(HumanReadableSerializerOptions.AddTypeAttribute)] = options => options.AddTypeAttribute(type => true, new HumanReadableConverterAttribute(typeof(DummyConverter))),
    };

    [Theory]
    [MemberData(nameof(Mutators))]
    public void ReadOnly_MutatorsThrow(string mutator)
    {
        var options = new HumanReadableSerializerOptions();
        options.MakeReadOnly();

        Assert.Throws<InvalidOperationException>(() => MutatorActions[mutator](options));
    }

    [Theory]
    [MemberData(nameof(Mutators))]
    public void Serialize_MakesOptionsReadOnly(string mutator)
    {
        var options = new HumanReadableSerializerOptions();
        HumanReadableSerializer.Serialize(new Payload(), options);

        Assert.True(options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => MutatorActions[mutator](options));
    }

    [Fact]
    public void CloneShouldCopyFormattersAndAttributes()
    {
        var options = new HumanReadableSerializerOptions()
            .AddJsonFormatter(new Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatterOptions { WriteIndented = true });
        options.IgnoreMember<Payload>(payload => payload.Tags);
        options.AddAttribute(typeof(Payload), nameof(Payload.Name), new HumanReadablePropertyNameAttribute("DisplayName"));
        options.AddTypeAttribute(type => type == typeof(DateTime), new HumanReadableConverterAttribute(new ConstantConverter()));
        HumanReadableSerializer.Serialize(new Payload(), options);

        var clone = options with { };
        using var content = new StringContent("""{"a":1}""", encoding: null, "application/json");

        Assert.Equal("Id: 1\nDisplayName: test\nWhen: constant", HumanReadableSerializer.Serialize(new Payload(), clone));
        Assert.Equal("Headers:\n  Content-Type: application/json; charset=utf-8\nValue:\n  {\n    \"a\": 1\n  }", HumanReadableSerializer.Serialize(content, clone));
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("APPLICATION/JSON")]
    [InlineData("text/json")]
    [InlineData("application/problem+json")]
    public void GetFormatter_Json(string mediaType)
    {
        var options = new HumanReadableSerializerOptions().AddJsonFormatter();

        Assert.IsType<Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatter>(options.GetFormatter(mediaType));
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("TEXT/XML")]
    [InlineData("application/atom+xml")]
    public void GetFormatter_Xml(string mediaType)
    {
        var options = new HumanReadableSerializerOptions().AddXmlFormatter();

        Assert.IsType<Meziantou.Framework.HumanReadable.ValueFormatters.XmlFormatter>(options.GetFormatter(mediaType));
    }

    [Fact]
    public void GetFormatter_Unknown()
    {
        var options = new HumanReadableSerializerOptions().AddJsonFormatter().AddXmlFormatter();

        Assert.Null(options.GetFormatter("text/plain"));
    }

    private sealed class ConstantConverter : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => true;
        public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options) => writer.WriteValue("constant");
    }
}
