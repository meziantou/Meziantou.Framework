using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlFeatureRestrictionTests
{
    [Fact]
    public void Deserialize_Anchor_AllowedByDefault()
    {
        var yaml = "a: &x 1\n";

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml);

        Assert.NotNull(result);
        Assert.Equal(1, result["a"]);
    }

    [Fact]
    public void Deserialize_Anchor_Disallowed_Throws()
    {
        var yaml = "a: &x 1\n";
        var options = new YamlSerializerOptions { AllowAnchors = false };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options));

        Assert.Contains("anchors are not allowed", exception.Message);
    }

    [Fact]
    public void Deserialize_Alias_Disallowed_Throws()
    {
        var yaml = "a: &x 1\nb: *x\n";
        var options = new YamlSerializerOptions { AllowAliases = false };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options));

        Assert.Contains("aliases are not allowed", exception.Message);
    }

    [Fact]
    public void Deserialize_Alias_AllowedByDefault_ResolvesReference()
    {
        // Aliases to object references (mappings/sequences) are resolved when reference handling is enabled.
        var yaml =
            "a: &x\n" +
            "  k: v\n" +
            "b: *x\n";
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };

        var result = YamlSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(yaml, options);

        Assert.NotNull(result);
        Assert.Equal("v", result["a"]["k"]);
        Assert.Equal("v", result["b"]["k"]);
    }

    [Fact]
    public void Deserialize_Alias_Disallowed_ThrowsEvenWithReferenceHandling()
    {
        var yaml =
            "a: &x\n" +
            "  k: v\n" +
            "b: *x\n";
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve, AllowAliases = false };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(yaml, options));

        Assert.Contains("aliases are not allowed", exception.Message);
    }

    [Fact]
    public void Deserialize_Anchor_DisallowedButAliasAllowed_StillThrowsOnAnchor()
    {
        // An anchor declaration is rejected even when it is never aliased.
        var yaml = "a: &x 1\nb: *x\n";
        var options = new YamlSerializerOptions { AllowAnchors = false, AllowAliases = true };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options));

        Assert.Contains("anchors are not allowed", exception.Message);
    }

    [Fact]
    public void Deserialize_MergeKey_AllowedByDefault()
    {
        var yaml =
            "<<: { a: 1, b: 2 }\n" +
            "b: 5\n";

        var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml);

        Assert.NotNull(result);
        Assert.Equal(1, result["a"]);
        Assert.Equal(5, result["b"]);
    }

    [Fact]
    public void Deserialize_MergeKey_Disallowed_Throws()
    {
        var yaml =
            "<<: { a: 1, b: 2 }\n" +
            "b: 5\n";
        var options = new YamlSerializerOptions { AllowMergeKeys = false };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options));

        Assert.Contains("merge keys are not allowed", exception.Message);
    }

    [Fact]
    public void Dom_AnchorAlias_StillWorks_RegardlessOfOptions()
    {
        var yaml = """
field1: &data ABCD
field2: *data
""";

        var stream = YamlStream.Load(new StringReader(yaml));

        var mapping = (YamlMapping)stream[0].Contents!;
        Assert.Equal("data", ((YamlValue)mapping["field1"]!).Anchor);
    }
}
