#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlDuplicateKeyHandlingTests
{
    private sealed class DuplicateModel
    {
        public int Age { get; set; }
    }

    [Fact]
    public void Dictionary_DuplicateKey_Error_Throws()
    {
        var yaml = "a: 1\na: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.Error };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options));
        Assert.Contains("Duplicate", exception.Message);
    }

    [Fact]
    public void Dictionary_DuplicateKey_FirstWins()
    {
        var yaml = "a: 1\na: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.FirstWins };

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(1, result["a"]);
    }

    [Fact]
    public void Dictionary_DuplicateKey_LastWins()
    {
        var yaml = "a: 1\na: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins };

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(2, result["a"]);
    }

    [Fact]
    public void Dictionary_DuplicateKey_CaseInsensitive_UsesComparer()
    {
        var yaml = "A: 1\na: 2\n";
        var options = new YamlSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins,
        };

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(2, result["a"]);
    }

    [Fact]
    public void Object_DuplicateKey_FirstWins()
    {
        var yaml = "Age: 1\nAge: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.FirstWins };

        var result = YamlSerializer.Deserialize<DuplicateModel>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(1, result.Age);
    }

    [Fact]
    public void Object_DuplicateKey_LastWins()
    {
        var yaml = "Age: 1\nAge: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.LastWins };

        var result = YamlSerializer.Deserialize<DuplicateModel>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Age);
    }

    [Fact]
    public void Object_DuplicateKey_Error_Throws()
    {
        var yaml = "Age: 1\nAge: 2\n";
        var options = new YamlSerializerOptions { DuplicateKeyHandling = YamlDuplicateKeyHandling.Error };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<DuplicateModel>(yaml, options));
        Assert.Contains("Duplicate", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Object_DuplicateKey_DefaultOptions_Throws(bool useSourceGeneration)
    {
        var exception = Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyObjectModel>("Age: 1\nName: a\nAge: 2\n", new YamlSerializerOptions(), useSourceGeneration));

        Assert.Equal("(Lin: 2, Col: 0, Chr: 15) - (Lin: 2, Col: 3, Chr: 18): Duplicate mapping key 'Age'.", exception.Message);
    }

    [Theory]
    [InlineData(false, YamlDuplicateKeyHandling.FirstWins, 1)]
    [InlineData(true, YamlDuplicateKeyHandling.FirstWins, 1)]
    [InlineData(false, YamlDuplicateKeyHandling.LastWins, 2)]
    [InlineData(true, YamlDuplicateKeyHandling.LastWins, 2)]
    public void Object_DuplicateKey_UsesDuplicateKeyHandling(bool useSourceGeneration, YamlDuplicateKeyHandling handling, int expectedAge)
    {
        var options = new YamlSerializerOptions { DuplicateKeyHandling = handling };

        var result = Deserialize<DuplicateKeyObjectModel>("Age: 1\nName: a\nAge: 2\n", options, useSourceGeneration)!;
        Assert.Equal(expectedAge, result.Age);
        Assert.Equal("a", result.Name);

        var constructed = Deserialize<DuplicateKeyConstructorModel>("Age: 1\nName: a\nAge: 2\n", options, useSourceGeneration)!;
        Assert.Equal(expectedAge, constructed.Age);
        Assert.Equal("a", constructed.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Object_DuplicateKey_IsDetectedForEveryKind(bool useSourceGeneration)
    {
        var options = new YamlSerializerOptions();

        // Keys matching no member, and keys matching a member only when names are compared case-insensitively.
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyObjectModel>("extra: 1\nextra: 2\n", options, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyConstructorModel>("extra: 1\nextra: 2\nName: a\nAge: 1\n", options, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyObjectModel>("age: 1\nAge: 2\n", options with { PropertyNameCaseInsensitive = true }, useSourceGeneration));
        Assert.Throws<YamlException>(() => Deserialize<DuplicateKeyConstructorModel>("name: a\nName: b\nAge: 1\n", options with { PropertyNameCaseInsensitive = true }, useSourceGeneration));

        var result = Deserialize<DuplicateKeyObjectModel>("age: 1\nAge: 2\n", options, useSourceGeneration)!;
        Assert.Equal(2, result.Age);
    }

    private static T? Deserialize<T>(string yaml, YamlSerializerOptions options, bool useSourceGeneration)
        => useSourceGeneration
            ? YamlSerializer.Deserialize<T>(yaml, new DuplicateKeyHandlingContext(options))
            : YamlSerializer.Deserialize<T>(yaml, options);
}

internal sealed class DuplicateKeyObjectModel
{
    public int Age { get; set; }

    public string? Name { get; set; }
}

internal sealed class DuplicateKeyConstructorModel
{
    public DuplicateKeyConstructorModel(string name, int age)
    {
        Name = name;
        Age = age;
    }

    public string Name { get; }

    public int Age { get; }
}

[YamlSerializable(typeof(DuplicateKeyObjectModel))]
[YamlSerializable(typeof(DuplicateKeyConstructorModel))]
internal sealed partial class DuplicateKeyHandlingContext : YamlSerializerContext
{
    public DuplicateKeyHandlingContext(YamlSerializerOptions options)
        : base(options)
    {
    }
}
