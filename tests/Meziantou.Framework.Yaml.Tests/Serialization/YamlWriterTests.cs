using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlWriterTests
{
    [Fact]
    public void RootScalar_QuotesAndEscapesAsNeeded()
    {
        var cases = new (string Value, string ExpectedYaml)[]
        {
            ("plain", "plain"),
            ("", "''"),
            (" leading", "\" leading\""),
            ("trailing ", "\"trailing \""),
            ("a:b", "\"a:b\""),
            ("a#b", "\"a#b\""),
            ("a\nb", "\"a\\nb\""),
            ("\u0001", "\"\\u0001\""),
        };

        foreach (var @case in cases)
        {
            var writer = CreateWriter(new YamlSerializerOptions(), out var buffer);
            writer.WriteScalar(@case.Value);

            Assert.Equal(@case.ExpectedYaml, buffer.ToString());
        }
    }

    [Fact]
    public void PropertyName_WithDash_IsQuoted()
    {
        var writer = CreateWriter(new YamlSerializerOptions(), out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("-");
        writer.WriteScalar("x");
        writer.WriteEndMapping();

        Assert.Equal("\"-\": x", buffer.ToString());
    }

    [Fact]
    public void RootScalars_ForNumbersAndSpecialFloats_AreEmittedPlain()
    {
        var writer = CreateWriter(new YamlSerializerOptions(), out var buffer);

        writer.WriteStartSequence();
        writer.WriteScalar(42);
        writer.WriteScalar(1.5);
        writer.WriteScalar(double.PositiveInfinity);
        writer.WriteScalar(double.NegativeInfinity);
        writer.WriteScalar(double.NaN);
        writer.WriteEndSequence();

        Assert.Equal("- 42\n- 1.5\n- .inf\n- -.inf\n- .nan", buffer.ToString());
    }

    [Fact]
    public void Mapping_WithScalar_WritesOnSingleLine()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("a");
        writer.WriteScalar("1");
        writer.WriteEndMapping();

        Assert.Equal("a: 1", buffer.ToString());
    }

    [Fact]
    public void Mapping_WithNestedMapping_WritesIndentedBlock()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("parent");
        writer.WriteStartMapping();
        writer.WritePropertyName("child");
        writer.WriteScalar("x");
        writer.WriteEndMapping();
        writer.WriteEndMapping();

        Assert.Equal("parent:\n  child: x", buffer.ToString());
    }

    [Fact]
    public void Sequence_WithScalars_WritesDashLines()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartSequence();
        writer.WriteScalar("a");
        writer.WriteScalar("b");
        writer.WriteEndSequence();

        Assert.Equal("- a\n- b", buffer.ToString());
    }

    [Fact]
    public void Constructor_WithStringBuilder_WritesExpectedYaml()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var buffer = new System.Text.StringBuilder();
        var writer = new YamlWriter(buffer, options);

        writer.WriteStartMapping();
        writer.WritePropertyName("enabled");
        writer.WriteScalar(true);
        writer.WritePropertyName("port");
        writer.WriteScalar(5432);
        writer.WriteEndMapping();

        Assert.Equal("enabled: true\nport: 5432", buffer.ToString());
    }

    [Fact]
    public void CharacterScalar_WithSpecialCharacter_IsQuoted()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartSequence();
        writer.WriteScalar(':');
        writer.WriteEndSequence();

        Assert.Equal("- \":\"", buffer.ToString());
    }

    [Fact]
    public void EmptyContainers_AreWrittenInline()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("emptyMap");
        writer.WriteStartMapping();
        writer.WriteEndMapping();
        writer.WritePropertyName("emptySeq");
        writer.WriteStartSequence();
        writer.WriteEndSequence();
        writer.WriteEndMapping();

        Assert.Equal("emptyMap: {}\nemptySeq: []", buffer.ToString());
    }

    [Fact]
    public void SequenceItem_EmptyMapping_WritesInline()
    {
        var options = new YamlSerializerOptions { WriteIndented = true, IndentSize = 2 };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartSequence();
        writer.WriteStartMapping();
        writer.WriteEndMapping();
        writer.WriteEndSequence();

        Assert.Equal("- {}", buffer.ToString());
    }

    [Fact]
    public void NestedMapping_WithoutIndentation_UsesFlowStyleAndRoundTrips()
    {
        var options = new YamlSerializerOptions { WriteIndented = false };
        var yaml = YamlSerializer.Serialize(new Outer { Child = new Inner { Value = 1 }, Count = 2 }, options);

        Assert.Equal("{Child: {Value: 1}, Count: 2}\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<Outer>(yaml, options);
        Assert.NotNull(roundTrip);
        Assert.NotNull(roundTrip.Child);
        Assert.Equal(1, roundTrip.Child.Value);
        Assert.Equal(2, roundTrip.Count);
    }

    [Fact]
    public void DeeplyNestedMappings_WithoutIndentation_UseFlowStyleAndRoundTrip()
    {
        var options = new YamlSerializerOptions { WriteIndented = false };
        var yaml = YamlSerializer.Serialize(new Level1 { Level2 = new Level2 { Level3 = new Inner { Value = 42 } } }, options);

        Assert.Equal("{Level2: {Level3: {Value: 42}}}\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<Level1>(yaml, options);
        Assert.Equal(42, roundTrip?.Level2?.Level3?.Value);
    }

    [Fact]
    public void SequenceOfMappings_WithoutIndentation_UsesFlowStyleAndRoundTrips()
    {
        var options = new YamlSerializerOptions { WriteIndented = false };
        var yaml = YamlSerializer.Serialize(
            new ItemsContainer
            {
                Items = [new Item { A = 1, B = 2 }, new Item { A = 3, B = 4 }],
            },
            options);

        Assert.Equal("{Items: [{A: 1, B: 2}, {A: 3, B: 4}]}\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<ItemsContainer>(yaml, options);
        Assert.NotNull(roundTrip?.Items);
        Assert.HasCount(2, roundTrip.Items);
        Assert.Equal((1, 2), (roundTrip.Items[0].A, roundTrip.Items[0].B));
        Assert.Equal((3, 4), (roundTrip.Items[1].A, roundTrip.Items[1].B));
    }

    [Fact]
    public void SequenceOfSequences_WithoutIndentation_UsesFlowStyleAndRoundTrips()
    {
        var options = new YamlSerializerOptions { WriteIndented = false };
        var yaml = YamlSerializer.Serialize(new RowsContainer { Rows = [[1, 2], [3, 4]] }, options);

        Assert.Equal("{Rows: [[1, 2], [3, 4]]}\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<RowsContainer>(yaml, options);
        Assert.NotNull(roundTrip?.Rows);
        Assert.Equal([1, 2], roundTrip.Rows[0]);
        Assert.Equal([3, 4], roundTrip.Rows[1]);
    }

    [Fact]
    public void RootSequence_WithoutIndentation_UsesFlowStyleAndRoundTrips()
    {
        var options = new YamlSerializerOptions { WriteIndented = false };
        var yaml = YamlSerializer.Serialize(new[] { 1, 2, 3 }, options);

        Assert.Equal("[1, 2, 3]\n", yaml);
        Assert.Equal([1, 2, 3], YamlSerializer.Deserialize<int[]>(yaml, options));
    }

    [Fact]
    public void EmptyCollections_WithoutIndentation_UseFlowStyle()
    {
        var options = new YamlSerializerOptions { WriteIndented = false };
        var yaml = YamlSerializer.Serialize(new MapsContainer { Map = [], List = [] }, options);

        Assert.Equal("{Map: {}, List: []}\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<MapsContainer>(yaml, options);
        Assert.NotNull(roundTrip?.Map);
        Assert.Empty(roundTrip.Map);
        Assert.NotNull(roundTrip.List);
        Assert.Empty(roundTrip.List);
    }

    [Fact]
    public void ScalarsWithFlowIndicators_WithoutIndentation_AreQuotedAndRoundTrip()
    {
        var options = new YamlSerializerOptions { WriteIndented = false };
        var value = new TextContainer { Text = "a, b", Other = "x: y" };
        var yaml = YamlSerializer.Serialize(value, options);

        Assert.Equal("{Text: \"a, b\", Other: \"x: y\"}\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<TextContainer>(yaml, options);
        Assert.Equal("a, b", roundTrip?.Text);
        Assert.Equal("x: y", roundTrip?.Other);
    }

    [Fact]
    public void BlockSequenceItemStyle_WithoutIndentation_IsIgnored()
    {
        var options = new YamlSerializerOptions { WriteIndented = false, BlockSequenceMappingStyle = YamlSequenceItemStyle.Expanded };
        var yaml = YamlSerializer.Serialize(new ItemsContainer { Items = [new Item { A = 1, B = 2 }] }, options);

        Assert.Equal("{Items: [{A: 1, B: 2}]}\n", yaml);
    }

    [Fact]
    public void Anchors_WithoutIndentation_UseFlowStyleAndRoundTrip()
    {
        var options = new YamlSerializerOptions { WriteIndented = false, ReferenceHandling = YamlReferenceHandling.Preserve };
        var shared = new Item { A = 1, B = 2 };
        var yaml = YamlSerializer.Serialize(new ItemsContainer { Items = [shared, shared] }, options);

        Assert.Matches(@"^&\w+ \{Items: &\w+ \[&\w+ \{A: 1, B: 2\}, \*\w+\]\}\n$", yaml);

        var roundTrip = YamlSerializer.Deserialize<ItemsContainer>(yaml, options);
        Assert.NotNull(roundTrip?.Items);
        Assert.HasCount(2, roundTrip.Items);
        Assert.Same(roundTrip.Items[0], roundTrip.Items[1]);
        Assert.Equal((1, 2), (roundTrip.Items[0].A, roundTrip.Items[0].B));
    }

    [Fact]
    public void SequenceOfMappings_WithIndentSizeGreaterThanTwo_AlignsCompactItemsAndRoundTrips()
    {
        var options = new YamlSerializerOptions { IndentSize = 4 };
        var yaml = YamlSerializer.Serialize(new ItemsContainer { Items = [new Item { A = 1, B = 2 }] }, options);

        Assert.Equal("Items:\n    - A: 1\n      B: 2\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<ItemsContainer>(yaml, options);
        Assert.NotNull(roundTrip?.Items);
        var item = Assert.Single(roundTrip.Items);
        Assert.Equal((1, 2), (item.A, item.B));
    }

    [Fact]
    public void AnchoredSequenceItem_UsesExpandedStyleAndRoundTrips()
    {
        var options = new YamlSerializerOptions { ReferenceHandling = YamlReferenceHandling.Preserve };
        var shared = new Item { A = 1, B = 2 };
        var yaml = YamlSerializer.Serialize(new ItemsContainer { Items = [shared, shared] }, options);

        Assert.Matches("  - &\\w+\n    A: 1\n    B: 2\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<ItemsContainer>(yaml, options);
        Assert.NotNull(roundTrip?.Items);
        Assert.HasCount(2, roundTrip.Items);
        Assert.Same(roundTrip.Items[0], roundTrip.Items[1]);
        Assert.Equal((1, 2), (roundTrip.Items[0].A, roundTrip.Items[0].B));
    }

    private static YamlWriter CreateWriter(YamlSerializerOptions options, out StringWriter buffer)
    {
        buffer = new StringWriter();
        return new YamlWriter(buffer, options);
    }

    private sealed class Outer
    {
        public Inner? Child { get; set; }

        public int Count { get; set; }
    }

    private sealed class Inner
    {
        public int Value { get; set; }
    }

    private sealed class Level1
    {
        public Level2? Level2 { get; set; }
    }

    private sealed class Level2
    {
        public Inner? Level3 { get; set; }
    }

    private sealed class ItemsContainer
    {
        public List<Item>? Items { get; set; }
    }

    private sealed class Item
    {
        public int A { get; set; }

        public int B { get; set; }
    }

    private sealed class RowsContainer
    {
        public List<List<int>>? Rows { get; set; }
    }

    private sealed class MapsContainer
    {
        public Dictionary<string, int>? Map { get; set; }

        public List<int>? List { get; set; }
    }

    private sealed class TextContainer
    {
        public string? Text { get; set; }

        public string? Other { get; set; }
    }
}
