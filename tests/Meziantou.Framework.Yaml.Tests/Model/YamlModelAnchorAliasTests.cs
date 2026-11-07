using Meziantou.Framework.Yaml.Model;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests;
public sealed class YamlModelAnchorAliasTests
{
    [Fact]
    public void Load_ShouldResolveAnchorAlias_ByMaterializingACopy()
    {
        var yaml = """
field1: &data ABCD
field2: *data
""";

        var stream = YamlStream.Load(new StringReader(yaml));
        Assert.Single(stream);

        var mapping = (YamlMapping)stream[0].Contents!;

        var field1 = (YamlValue)mapping["field1"]!;
        var field2 = (YamlValue)mapping["field2"]!;

        Assert.Equal("data", field1.Anchor);
        Assert.Equal("ABCD", field1.Value);

        // The model API doesn't preserve aliases as a distinct node type: we materialize a copy.
        Assert.Null(field2.Anchor);
        Assert.Equal("ABCD", field2.Value);
    }

    /// <summary>
    /// Nesting anchors so that each level references the previous one several times makes the node count grow as
    /// fan^levels while the document grows linearly. Before the expansion budget, the 397-byte payload below
    /// allocated about 3.9 GB and the 492-byte one exhausted memory.
    /// </summary>
    private static string CreateAliasBomb(int levels, int fan = 9)
    {
        var builder = new StringBuilder();
        builder.Append("a0: &a0 \"xxxxxxxx\"\n");
        for (var i = 1; i <= levels; i++)
        {
            builder.Append(FormattableString.Invariant($"a{i}: &a{i} ["));
            for (var j = 0; j < fan; j++)
            {
                if (j > 0)
                {
                    builder.Append(',');
                }

                builder.Append(FormattableString.Invariant($"*a{i - 1}"));
            }

            builder.Append("]\n");
        }

        builder.Append(FormattableString.Invariant($"root: *a{levels}\n"));
        return builder.ToString();
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void Load_AliasBomb_IsRejectedInsteadOfExhaustingMemory(int levels)
    {
        var yaml = CreateAliasBomb(levels);

        var exception = Assert.Throws<YamlException>(() => YamlStream.Load(new StringReader(yaml)));

        Assert.Contains("alias expansion", exception.Message);
    }

    [Fact]
    public void Deserialize_YamlElement_AliasBomb_IsRejectedInsteadOfExhaustingMemory()
    {
        // The converter path builds the same model and had the same defect.
        var yaml = CreateAliasBomb(10);

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<YamlElement>(yaml));

        Assert.Contains("alias expansion", exception.Message);
    }

    [Fact]
    public void Load_WithAllowAliasesFalse_RejectsAliases()
    {
        // Previously unreachable: YamlStream.Load had no overload accepting options.
        var options = new YamlSerializerOptions { AllowAliases = false };

        var exception = Assert.Throws<YamlException>(() => YamlStream.Load(new StringReader("a: &x 1\nb: *x"), options));

        Assert.Contains("aliases are not allowed", exception.Message);
    }

    [Fact]
    public void Load_WithAllowAnchorsFalse_RejectsAnchors()
    {
        var options = new YamlSerializerOptions { AllowAnchors = false };

        var exception = Assert.Throws<YamlException>(() => YamlStream.Load(new StringReader("a: &x 1\nb: *x"), options));

        Assert.Contains("anchors are not allowed", exception.Message);
    }

    [Fact]
    public void Load_WithCustomAliasExpansionBudget_IsEnforced()
    {
        var options = new YamlSerializerOptions { MaxAliasExpansionNodeCount = 4 };

        var exception = Assert.Throws<YamlException>(() => YamlStream.Load(new StringReader(CreateAliasBomb(4)), options));

        Assert.Contains("alias expansion", exception.Message);
    }

    [Fact]
    public void Load_WithLargeAliasExpansionBudget_AllowsHeavyButBoundedExpansion()
    {
        var yaml = CreateAliasBomb(4);
        var options = new YamlSerializerOptions { MaxAliasExpansionNodeCount = int.MaxValue };

        var stream = YamlStream.Load(new StringReader(yaml), options);

        Assert.Single(stream);
    }

    [Fact]
    public void Load_SharedAnchorReferencedSeveralTimes_StillWorks()
    {
        // The budget must not break ordinary anchor reuse, which is the common real-world case.
        var yaml = """
                   base: &b {name: app, port: 80}
                   a: *b
                   b: *b
                   c: *b
                   """;

        var stream = YamlStream.Load(new StringReader(yaml));

        var mapping = (YamlMapping)stream[0].Contents!;
        Assert.HasCount(4, mapping.Keys);
        Assert.Equal("app", ((YamlValue)((YamlMapping)mapping["a"]!)["name"]!).Value);
    }

    [Fact]
    public void Load_MergeKeys_StillWork()
    {
        var yaml = """
                   defaults: &d {a: 1, b: 2}
                   item:
                     <<: *d
                     c: 3
                   """;

        var stream = YamlStream.Load(new StringReader(yaml));

        Assert.Single(stream);
    }

    [Fact]
    public void Load_LargeDocumentWithoutAliases_IsNotAffectedByTheBudget()
    {
        // Only nodes produced by alias expansion are charged, so an alias-free document of any size is unaffected.
        var builder = new StringBuilder();
        for (var i = 0; i < 50_000; i++)
        {
            builder.Append(FormattableString.Invariant($"k{i}: v{i}\n"));
        }

        var stream = YamlStream.Load(new StringReader(builder.ToString()));

        Assert.HasCount(50_000, ((YamlMapping)stream[0].Contents!).Keys);
    }

    [Fact]
    public void MaxAliasExpansionNodeCount_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlSerializerOptions { MaxAliasExpansionNodeCount = -1 });
    }
}
