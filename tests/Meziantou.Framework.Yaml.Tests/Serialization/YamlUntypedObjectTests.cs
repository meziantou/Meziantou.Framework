namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlUntypedObjectTests
{
    [Fact]
    public void DeserializeObject_InfersScalarTypes()
    {
        Assert.Equal(true, YamlSerializer.Deserialize<object>("true"));
        Assert.Equal(42L, YamlSerializer.Deserialize<object>("42"));
        Assert.Equal(1.5d, (double)YamlSerializer.Deserialize<object>("1.5")!, 1e-12);
        Assert.Equal("text", YamlSerializer.Deserialize<object>("text"));
        Assert.Null(YamlSerializer.Deserialize<object>("null"));
        Assert.Null(YamlSerializer.Deserialize<object>("~"));
    }

    [Fact]
    public void DeserializeObject_InfersSequenceAndMapping()
    {
        var list = (List<object?>)YamlSerializer.Deserialize<object>("- 1\n- true\n- text\n- null\n")!;
        Assert.HasCount(4, list);
        Assert.Equal(1L, list[0]);
        Assert.Equal(true, list[1]);
        Assert.Equal("text", list[2]);
        Assert.Null(list[3]);

        var dict = (Dictionary<string, object?>)YamlSerializer.Deserialize<object>("a: 1\nb: true\nc: text\n")!;
        Assert.HasCount(3, dict);
        Assert.Equal(1L, dict["a"]);
        Assert.Equal(true, dict["b"]);
        Assert.Equal("text", dict["c"]);
    }

    [Fact]
    public void UnsafeTagActivation_IsOptIn()
    {
        var yaml = "!System.Int32 42\n";

        var defaultValue = YamlSerializer.Deserialize<object>(yaml);
        Assert.Equal(42L, defaultValue);

        var unsafeValue = YamlSerializer.Deserialize<object>(
            yaml,
            new YamlSerializerOptions { UnsafeAllowDeserializeFromTagTypeName = true });
        Assert.Equal(42, unsafeValue);
        Assert.IsType<int>(unsafeValue);
    }

    [Fact]
    public void UnsafeTagActivation_HandlesMscorlibTypeNames()
    {
        var yaml = "!System.Int32,mscorlib 42\n";
        var value = YamlSerializer.Deserialize<object>(
            yaml,
            new YamlSerializerOptions { UnsafeAllowDeserializeFromTagTypeName = true });

        Assert.Equal(42, value);
        Assert.IsType<int>(value);
    }

    [Fact]
    public void UnsafeTagActivation_IgnoresUnknownTypes()
    {
        var yaml = "!NoSuch.Type 42\n";
        var value = YamlSerializer.Deserialize<object>(
            yaml,
            new YamlSerializerOptions { UnsafeAllowDeserializeFromTagTypeName = true });

        Assert.Equal(42L, value);
    }

    [Fact]
    public void DeserializeObject_AliasWithoutPreserve_ThrowsYamlException()
    {
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object>("*id001\n"));
        Assert.Contains("ReferenceHandling", ex.Message);
        Assert.Contains("Preserve", ex.Message);
    }
}
