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
}
