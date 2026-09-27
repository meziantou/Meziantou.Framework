using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public class NewApiMetadataStoreRoundtripTests
{
    private sealed class Sample
    {
        public int Value { get; set; }
    }

    [Fact]
    public void MetadataStore_DeserializeThenSerialize_PreservesTriviaAndHexIntegers()
    {
        var store = new TomlMetadataStore();
        var options = new TomlSerializerOptions
        {
            MetadataStore = store,
        };

        var sample = TomlSerializer.Deserialize<Sample>(
            """
            # Leading comment
            Value = 0x2A # Trailing comment
            """,
            options);

        var toml = TomlSerializer.Serialize(sample, options);

        Assert.Contains("# Leading comment", toml);
        Assert.Contains("# Trailing comment", toml);
        Assert.Contains("Value = 0x", toml);
    }

    [Theory]
    [InlineData("# header comment\n[server]  # trailing header\n# before key\nport = 80 # trailing kv\n# before second\nhost = 'x'\n")]
    [InlineData("[tool]\nk = 1\n[tool.tool]\n# c\nx = 1\n")]
    [InlineData("[a]\n# about a.a\na = 1 # trailing\n")]
    [InlineData("# first\n[[items]] # one\nname = 'a'\n")]
    [InlineData("[server]\nport = 1\n# inner\n[server.server]\nport = 2\n")]
    public void MetadataStore_CommentsAroundHeaders_RoundTrip(string toml)
    {
        var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() };

        var output = TomlSerializer.Serialize(TomlSerializer.Deserialize<Model.TomlTable>(toml, options), options);

        Assert.Equal(toml, output);
    }

    [Fact]
    public void MetadataStore_CommentsAroundArrayOfTablesHeaders_AreKept()
    {
        var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() };
        var toml = "# first\n[[items]]\nname = 'a'\n\n# second\n[[items]] # two\nname = 'b'\n";

        var output = TomlSerializer.Serialize(TomlSerializer.Deserialize<Model.TomlTable>(toml, options), options);

        // The second header has no key of its own to hold its comments, so they move to its first key
        Assert.Equal("# first\n[[items]]\nname = 'a'\n\n[[items]]\n# second\n# two\nname = 'b'\n", output);
    }

    public static TheoryData<string, TomlInlineTablePolicy, TomlTableArrayStyle> CorpusWithLayouts()
    {
        var data = new TheoryData<string, TomlInlineTablePolicy, TomlTableArrayStyle>();
        foreach (var name in StandardTests.ListToml11CaseNames("valid"))
        {
            if (StandardTests.GetCase(name).Toml is { } toml && toml.Contains('#', StringComparison.Ordinal))
            {
                foreach (var policy in Enum.GetValues<TomlInlineTablePolicy>())
                {
                    foreach (var style in Enum.GetValues<TomlTableArrayStyle>())
                    {
                        data.Add(name, policy, style);
                    }
                }
            }
        }

        return data;
    }

    /// <summary>Comments are written on their own lines, so they never hide a key, a header or a value.</summary>
    [Theory]
    [MemberData(nameof(CorpusWithLayouts))]
    public void MetadataStore_CorpusWithComments_KeepsTheData(string name, TomlInlineTablePolicy policy, TomlTableArrayStyle style)
    {
        var toml = StandardTests.GetCase(name).Toml!;
        var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore(), InlineTablePolicy = policy, TableArrayStyle = style };
        var model = TomlSerializer.Deserialize<Model.TomlTable>(toml, options)!;

        var output = TomlSerializer.Serialize(model, options);

        Assert.False(Parsing.SyntaxParser.Parse(output).HasErrors, output);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(ModelHelper.ToJson(model), ModelHelper.ToJson(TomlSerializer.Deserialize<Model.TomlTable>(output))), output);
    }
}
