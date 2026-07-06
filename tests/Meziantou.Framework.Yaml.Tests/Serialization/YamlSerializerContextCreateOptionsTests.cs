using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlSerializerContextCreateOptionsTests
{
    [Fact]
    public void CreateOptions_OverridesSourceName()
    {
        var context = new TestYamlSerializerContext();
        var options = context.CreateOptions(o => o with { SourceName = "config.yaml" });

        Assert.Equal("config.yaml", options.SourceName);
        Assert.Same(context, options.TypeInfoResolver);
    }

    [Fact]
    public void CreateOptions_PreservesTypeInfoResolver()
    {
        var context = new TestYamlSerializerContext();
        var options = context.CreateOptions(o => o with { WriteIndented = false });

        Assert.Same(context, options.TypeInfoResolver);
        Assert.False(options.WriteIndented);
    }

    [Fact]
    public void CreateOptions_OverwritesResolverIfDifferent()
    {
        var context1 = new TestYamlSerializerContext();
        var context2 = new TestYamlSerializerContext();

        // Even if configure tries to set a different resolver, CreateOptions overwrites it
        var options = context1.CreateOptions(o => o with { TypeInfoResolver = context2 });

        Assert.Same(context1, options.TypeInfoResolver);
    }

    [Fact]
    public void CreateOptions_PreservesOriginalConverters()
    {
        var converter = new DummyConverter();
        var baseOptions = new YamlSerializerOptions { Converters = [converter] };
        var context = new TestYamlSerializerContext(baseOptions);

        var options = context.CreateOptions(o => o with { SourceName = "test.yaml" });

        Assert.Equal(1, options.Converters.Count);
        Assert.Same(converter, options.Converters[0]);
        Assert.Equal("test.yaml", options.SourceName);
    }

    [Fact]
    public void CreateOptions_CanAddConverters()
    {
        var context = new TestYamlSerializerContext();
        var newConverter = new DummyConverter();

        var options = context.CreateOptions(o => o with
        {
            Converters = [newConverter],
            SourceName = "extra.yaml",
        });

        Assert.Equal(1, options.Converters.Count);
        Assert.Same(newConverter, options.Converters[0]);
        Assert.Equal("extra.yaml", options.SourceName);
        Assert.Same(context, options.TypeInfoResolver);
    }

    [Fact]
    public void CreateOptions_CanOverrideMultipleProperties()
    {
        var context = new TestYamlSerializerContext();
        var options = context.CreateOptions(o => o with
        {
            SourceName = "multi.yaml",
            WriteIndented = false,
            PropertyNameCaseInsensitive = true,
            Schema = YamlSchemaKind.Extended,
        });

        Assert.Equal("multi.yaml", options.SourceName);
        Assert.False(options.WriteIndented);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal(YamlSchemaKind.Extended, options.Schema);
        Assert.Same(context, options.TypeInfoResolver);
    }

    [Fact]
    public void CreateOptions_IdentityReturnsOptionsWithSameResolver()
    {
        var context = new TestYamlSerializerContext();
        var options = context.CreateOptions(o => o);

        // Identity transform preserves the resolver
        Assert.Same(context, options.TypeInfoResolver);
    }

    [Fact]
    public void CreateOptions_WorksWithOptionsBasedContext()
    {
        var baseOptions = new YamlSerializerOptions
        {
            SourceName = "original.yaml",
            WriteIndented = false,
        };
        var context = new TestYamlSerializerContext(baseOptions);

        // Override just SourceName, keep other base settings
        var options = context.CreateOptions(o => o with { SourceName = "override.yaml" });

        Assert.Equal("override.yaml", options.SourceName);
        Assert.False(options.WriteIndented); // Preserved from base
        Assert.Same(context, options.TypeInfoResolver);
    }

    [Fact]
    public void CreateOptions_ResultCanBeUsedForDeserialization()
    {
        var context = new TestYamlSerializerContext();
        var options = context.CreateOptions(o => o with { SourceName = "test-input.yaml" });

        // Verify the options work for actual deserialization
        var yaml = "first_name: hello\nAge: 42\n";
        var result = YamlSerializer.Deserialize<GeneratedPerson>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal("hello", result.FirstName);
    }

    [Fact]
    public void CreateOptions_SourceNameAppearsInErrorMessages()
    {
        var context = new TestYamlSerializerContext();
        var options = context.CreateOptions(o => o with { SourceName = "myfile.yaml" });

        var yaml = "first_name: hello\nAge: not-a-number\n";
        var ex = Assert.Throws<YamlException>(() =>
            YamlSerializer.Deserialize<GeneratedPerson>(yaml, options));

        Assert.Contains("myfile.yaml", ex.Message);
    }

    private sealed class DummyConverter : YamlConverter<int>
    {
        public override int Read(YamlReader reader)
        {
            reader.Skip();
            return 0;
        }

        public override void Write(YamlWriter writer, int value)
            => writer.WriteScalar("0");
    }
}
