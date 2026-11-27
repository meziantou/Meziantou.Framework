using Meziantou.Framework.Yaml.Nodes;

namespace Meziantou.Framework.Yaml.Tests.Nodes;

public sealed class YamlStreamPreservingFormattingTests
{
    private const string Source = """
        # Service definition
        name:   web    # the name
        image: "nginx:1.25"
        ports:
          - 80   # http
          - 443

        env:
          LEVEL: info
          # retries
          RETRIES: 3
        flow: {a: 1, b: [x, y]}
        script: |
          echo one
          echo two
        """;

    [Fact]
    public void ReadmeExample_PreservingFormatting()
    {
        #region YamlStream_PreservingFormatting
        var stream = YamlStream.Load(new StringReader("""
            # Deployment settings
            image: nginx:1.25   # pinned version
            replicas: 2
            env:
              LEVEL: info
            """));

        var root = (YamlMapping)stream[0].Contents!;
        root["image"] = new YamlValue("nginx:1.27");
        ((YamlMapping)root["env"]!).Add(new YamlValue("MODE"), new YamlValue("fast"));

        // The comments and the formatting of the nodes that did not change are kept
        Assert.Equal("""
            # Deployment settings
            image: nginx:1.27   # pinned version
            replicas: 2
            env:
              LEVEL: info
              MODE: fast
            """, stream.ToStringPreservingFormatting());
        #endregion
    }

    [Fact]
    public void Unchanged_IsTheSourceText()
    {
        var stream = Load(Source);
        Assert.Equal(Source, stream.ToStringPreservingFormatting());
    }

    [Fact]
    public void ChangedScalar_ReplacesOnlyItsText()
    {
        var stream = Load(Source);
        var root = Root(stream);
        root["name"] = new YamlValue("api");
        Assert.Equal(Source.Replace("name:   web    # the name", "name:   api    # the name", StringComparison.Ordinal), stream.ToStringPreservingFormatting());
    }

    [Fact]
    public void ChangedNestedScalar_ReplacesOnlyItsText()
    {
        var stream = Load(Source);
        var env = (YamlMapping)Root(stream)["env"]!;
        env["LEVEL"] = new YamlValue("debug");
        Assert.Equal(Source.Replace("LEVEL: info", "LEVEL: debug", StringComparison.Ordinal), stream.ToStringPreservingFormatting());
    }

    [Fact]
    public void AddedEntry_IsWrittenAtTheEndOfTheMapping()
    {
        var stream = Load(Source);
        var env = (YamlMapping)Root(stream)["env"]!;
        env.Add(new YamlValue("MODE"), new YamlValue("fast"));
        Assert.Equal(Source.Replace("  RETRIES: 3\n", "  RETRIES: 3\n  MODE: fast\n", StringComparison.Ordinal), stream.ToStringPreservingFormatting());
    }

    [Fact]
    public void RemovedEntry_RemovesItsLines()
    {
        var stream = Load(Source);
        var env = (YamlMapping)Root(stream)["env"]!;
        env.Remove("LEVEL");
        Assert.Equal(Source.Replace("  LEVEL: info\n", "", StringComparison.Ordinal), stream.ToStringPreservingFormatting());
    }

    [Fact]
    public void AddedAndRemovedItems_KeepTheOtherItems()
    {
        var stream = Load(Source);
        var ports = (YamlSequence)Root(stream)["ports"]!;
        ports.RemoveAt(1);
        ports.Add(new YamlValue("8080"));
        Assert.Equal(Source.Replace("  - 443\n", "  - 8080\n", StringComparison.Ordinal), stream.ToStringPreservingFormatting());
    }

    [Fact]
    public void ChangedFlowCollection_RewritesOnlyTheCollection()
    {
        var stream = Load(Source);
        var flow = (YamlMapping)Root(stream)["flow"]!;
        flow.Add(new YamlValue("c"), new YamlValue("3"));
        var result = stream.ToStringPreservingFormatting();
        Assert.Equal(Source.Replace("flow: {a: 1, b: [x, y]}", "flow: {a: 1, b: [x, y], c: 3}", StringComparison.Ordinal), result);
    }

    [Fact]
    public void ScalarReplacedByMapping_IsWrittenAsABlockMapping()
    {
        var stream = Load(Source);
        Root(stream)["name"] = new YamlMapping { { new YamlValue("first"), new YamlValue("a") }, { new YamlValue("last"), new YamlValue("b") } };
        var result = stream.ToStringPreservingFormatting();
        Assert.Equal(Source.Replace("name:   web    # the name", "name:   \n  first: a\n  last: b    # the name", StringComparison.Ordinal), result);
        AssertSameData(stream, result);
    }

    [Fact]
    public void ChangedAliasCopy_ReplacesTheAlias()
    {
        var yaml = "base: &b {x: 1}\ncopy: *b\nother: *b\n";
        var stream = Load(yaml);
        var copy = (YamlMapping)Root(stream)["copy"]!;
        copy["x"] = new YamlValue("2");
        var result = stream.ToStringPreservingFormatting();
        Assert.Equal("base: &b {x: 1}\ncopy: {x: 2}\nother: *b\n", result);
    }

    [Theory]
    [MemberData(nameof(Utf8YamlReaderTests.ValidYamlTestSuiteCases), MemberType = typeof(Utf8YamlReaderTests))]
    public void YamlTestSuite_Unchanged_IsTheSourceText(string id)
    {
        var yaml = YamlTestSuite.Get(id).Yaml;
        var stream = Load(yaml);
        Assert.Equal(yaml, stream.ToStringPreservingFormatting());
    }

    [Theory]
    [MemberData(nameof(Utf8YamlReaderTests.ValidYamlTestSuiteCases), MemberType = typeof(Utf8YamlReaderTests))]
    public void YamlTestSuite_ChangedScalars_KeepTheOtherNodes(string id)
    {
        var yaml = YamlTestSuite.Get(id).Yaml;
        var stream = Load(yaml);
        var changed = false;
        foreach (var document in stream)
        {
            changed |= ChangeEveryOtherScalar(document.Contents, document, null);
        }

        if (!changed)
            return;

        AssertSameData(stream, stream.ToStringPreservingFormatting());
    }

    private static bool ChangeEveryOtherScalar(YamlNode? node, object parent, object? key)
    {
        switch (node)
        {
            case YamlValue value when value.Scalar.Value.Length > 0 && value.Tag is null:
                var replacement = new YamlValue("changed " + value.Scalar.Value);
                switch (parent)
                {
                    case YamlMapping mapping:
                        mapping[(YamlNode)key!] = replacement;
                        break;
                    case YamlSequence sequence:
                        sequence[(int)key!] = replacement;
                        break;
                    case YamlStreamDocument document:
                        document.Contents = replacement;
                        break;
                }

                return true;

            case YamlMapping mapping:
                var changed = false;
                foreach (var entry in mapping.ToList())
                {
                    changed |= ChangeEveryOtherScalar(entry.Value, mapping, entry.Key);
                }

                return changed;

            case YamlSequence sequence:
                var changedItem = false;
                for (var i = 0; i < sequence.Count; i += 2)
                {
                    changedItem |= ChangeEveryOtherScalar(sequence[i], sequence, i);
                }

                return changedItem;

            default:
                return false;
        }
    }

    // The written text loads to the same data as the changed stream
    private static void AssertSameData(YamlStream stream, string written)
    {
        var expected = YamlTestSuite.ReadRepresentation(ToYaml(stream));
        string actual;
        try
        {
            actual = YamlTestSuite.ReadRepresentation(written);
        }
        catch (YamlException ex)
        {
            throw new InvalidOperationException("The written YAML is invalid: " + ex.Message + "\n" + written, ex);
        }

        if (expected != actual)
        {
            Assert.Fail("The written YAML has different data:\n" + written + "\n--- expected\n" + expected + "\n--- actual\n" + actual);
        }
    }

    private static string ToYaml(YamlStream stream)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.WriteTo(writer);
        return writer.ToString();
    }

    private static YamlStream Load(string yaml) => YamlStream.Load(new StringReader(yaml), new YamlSerializerOptions { MaxAliasExpansionNodeCount = 100_000 });

    private static YamlMapping Root(YamlStream stream) => (YamlMapping)stream[0].Contents!;
}
