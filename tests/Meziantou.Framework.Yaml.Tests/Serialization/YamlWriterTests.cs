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

    [Fact]
    public void IndentBlockSequences_IsEnabledByDefault()
    {
        Assert.True(new YamlSerializerOptions().IndentBlockSequences);
    }

    [Fact]
    public void MappingValueSequence_WithIndentBlockSequencesDisabled_WritesDashesAtParentIndentation()
    {
        var options = new YamlSerializerOptions { IndentBlockSequences = false };
        var writer = CreateWriter(options, out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("tags");
        writer.WriteStartSequence();
        writer.WriteScalar("a");
        writer.WriteScalar("b");
        writer.WriteEndSequence();
        writer.WriteEndMapping();

        Assert.Equal("tags:\n- a\n- b", buffer.ToString());
    }

    [Fact]
    public void MappingValueSequence_WithIndentBlockSequencesDisabled_KeepsMappingsIndentedAndRoundTrips()
    {
        var options = new YamlSerializerOptions { IndentBlockSequences = false };
        var yaml = YamlSerializer.Serialize(new ItemsContainer { Items = [new Item { A = 1, B = 2 }] }, options);

        Assert.Equal("Items:\n- A: 1\n  B: 2\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<ItemsContainer>(yaml, options);
        Assert.NotNull(roundTrip?.Items);
        var item = Assert.Single(roundTrip.Items);
        Assert.Equal((1, 2), (item.A, item.B));
    }

    [Fact]
    public void MappingValueSequence_WithIndentBlockSequencesDisabledAndLargeIndentSize_IgnoresIndentSizeForSequences()
    {
        var options = new YamlSerializerOptions { IndentBlockSequences = false, IndentSize = 4 };
        var yaml = YamlSerializer.Serialize(new Outer { Child = new Inner { Value = 1 }, Count = 2 }, options);

        Assert.Equal("Child:\n    Value: 1\nCount: 2\n", yaml);

        var listYaml = YamlSerializer.Serialize(new MapsContainer { List = [1, 2] }, options);
        Assert.Equal("Map: null\nList:\n- 1\n- 2\n", listYaml);
    }

    [Fact]
    public void NestedSequence_WithIndentBlockSequencesDisabled_IsStillIndentedAndRoundTrips()
    {
        var options = new YamlSerializerOptions { IndentBlockSequences = false };
        var yaml = YamlSerializer.Serialize(new RowsContainer { Rows = [[1, 2], [3]] }, options);

        Assert.Equal("Rows:\n-\n  - 1\n  - 2\n-\n  - 3\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<RowsContainer>(yaml, options);
        Assert.NotNull(roundTrip?.Rows);
        Assert.HasCount(2, roundTrip.Rows);
        Assert.Equal([1, 2], roundTrip.Rows[0]);
        Assert.Equal([3], roundTrip.Rows[1]);
    }

    [Fact]
    public void SequenceInsideCompactSequenceItem_WithIndentBlockSequencesDisabled_AlignsWithItsMappingAndRoundTrips()
    {
        var options = new YamlSerializerOptions { IndentBlockSequences = false };
        var yaml = YamlSerializer.Serialize(new TagsContainer { Items = [new TaggedItem { Name = "a", Tags = ["x", "y"] }] }, options);

        Assert.Equal("Items:\n- Name: a\n  Tags:\n  - x\n  - y\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<TagsContainer>(yaml, options);
        Assert.NotNull(roundTrip?.Items);
        var item = Assert.Single(roundTrip.Items);
        Assert.Equal("a", item.Name);
        Assert.Equal(["x", "y"], item.Tags);
    }

    [Fact]
    public void IndentBlockSequences_WithoutIndentation_IsIgnored()
    {
        var options = new YamlSerializerOptions { WriteIndented = false, IndentBlockSequences = false };
        var yaml = YamlSerializer.Serialize(new ItemsContainer { Items = [new Item { A = 1, B = 2 }] }, options);

        Assert.Equal("{Items: [{A: 1, B: 2}]}\n", yaml);
    }

    [Fact]
    public void StringStyle_DefaultsToAny()
    {
        Assert.Equal(ScalarStyle.Any, new YamlSerializerOptions().ScalarStylePreferences.StringStyle);
    }

    [Fact]
    public void StringStyle_WithUndefinedValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlScalarStylePreferences { StringStyle = (ScalarStyle)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateWriter(new YamlSerializerOptions(), out _).PushStringStyle((ScalarStyle)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new YamlStringStyleAttribute((ScalarStyle)42));
    }

    [Fact]
    public void StringStyle_Literal_WritesBlockScalarAndRoundTrips()
    {
        var value = new TextContainer { Text = "line1\nline2", Other = "end" };
        var yaml = YamlSerializer.Serialize(value, StringStyleOptions(ScalarStyle.Literal));

        Assert.Equal("Text: |-\n  line1\n  line2\nOther: |-\n  end\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<TextContainer>(yaml, StringStyleOptions(ScalarStyle.Literal));
        Assert.Equal("line1\nline2", roundTrip?.Text);
        Assert.Equal("end", roundTrip?.Other);
    }

    [Fact]
    public void StringStyle_Literal_PicksTheChompingIndicatorFromTheTrailingLineBreaks()
    {
        var cases = new (string Value, string ExpectedIndicator)[]
        {
            ("a\nb", "|-"),
            ("a\nb\n", "|"),
            ("a\nb\n\n", "|+"),
            ("a\nb\n\n\n", "|+"),
        };

        foreach (var @case in cases)
        {
            var options = StringStyleOptions(ScalarStyle.Literal);
            var yaml = YamlSerializer.Serialize(new TextContainer { Text = @case.Value }, options);

            Assert.StartsWith("Text: " + @case.ExpectedIndicator + "\n", yaml);
            Assert.Equal(@case.Value, YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
        }
    }

    [Fact]
    public void StringStyle_Literal_KeepChompingRoundTripsBeforeAnotherKeyAndAtTheEndOfTheDocument()
    {
        var options = StringStyleOptions(ScalarStyle.Literal);

        var tail = YamlSerializer.Serialize(new TextContainer { Text = "a\n\n" }, options);
        Assert.Equal("Text: |+\n  a\n\nOther: null\n", tail);
        Assert.Equal("a\n\n", YamlSerializer.Deserialize<TextContainer>(tail, options)?.Text);

        var followed = YamlSerializer.Serialize(new TextContainer { Other = "a\n\n" }, options);
        Assert.Equal("Text: null\nOther: |+\n  a\n\n", followed);
        Assert.Equal("a\n\n", YamlSerializer.Deserialize<TextContainer>(followed, options)?.Other);
    }

    [Fact]
    public void StringStyle_Literal_WithLeadingBlank_WritesAnIndentationIndicator()
    {
        var options = StringStyleOptions(ScalarStyle.Literal);
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "  indented\nplain" }, options);

        Assert.Equal("Text: |2-\n    indented\n  plain\nOther: null\n", yaml);
        Assert.Equal("  indented\nplain", YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
    }

    [Fact]
    public void StringStyle_Literal_KeepsMoreIndentedLinesAndBlankLines()
    {
        var options = StringStyleOptions(ScalarStyle.Literal);
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "if x:\n  y\n\nz" }, options);

        Assert.Equal("Text: |-\n  if x:\n    y\n\n  z\nOther: null\n", yaml);
        Assert.Equal("if x:\n  y\n\nz", YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
    }

    [Fact]
    public void StringStyle_Literal_WithValueThatWouldNotRoundTrip_FallsBackToTheAutomaticStyle()
    {
        // An empty value, a blank at the end of a line, a carriage return, a control character, and a line that
        // starts with a tab all lose information when they are written as a block scalar.
        var cases = new[] { "", "a \nb", "a\nb ", "a\r\nb", "a\n\u0001", "a\n\tb" };

        foreach (var value in cases)
        {
            var options = StringStyleOptions(ScalarStyle.Literal);
            var yaml = YamlSerializer.Serialize(new TextContainer { Text = value }, options);

            Assert.DoesNotContain("Text: |", yaml);
            Assert.Equal(value, YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
        }
    }

    [Fact]
    public void StringStyle_Literal_InSequenceItemsAndNestedMappings_RoundTrips()
    {
        var options = StringStyleOptions(ScalarStyle.Literal);
        var yaml = YamlSerializer.Serialize(new StringsContainer { Items = ["a\nb", "c"], Nested = new TextContainer { Text = "d\ne" } }, options);

        Assert.Equal("Items:\n  - |-\n    a\n    b\n  - |-\n    c\nNested:\n  Text: |-\n    d\n    e\n  Other: null\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<StringsContainer>(yaml, options);
        Assert.Equal(["a\nb", "c"], roundTrip?.Items);
        Assert.Equal("d\ne", roundTrip?.Nested?.Text);
    }

    [Fact]
    public void StringStyle_Literal_WithIndentSizeGreaterThanTwo_IndentsTheContent()
    {
        var options = StringStyleOptions(ScalarStyle.Literal) with { IndentSize = 4 };
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "a\nb" }, options);

        Assert.Equal("Text: |-\n    a\n    b\nOther: null\n", yaml);
        Assert.Equal("a\nb", YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
    }

    [Fact]
    public void StringStyle_Folded_WritesBlankLinesForTheLineBreaksAndRoundTrips()
    {
        var options = StringStyleOptions(ScalarStyle.Folded);
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "a\nb\n\nc" }, options);

        Assert.Equal("Text: >-\n  a\n\n  b\n\n\n  c\nOther: null\n", yaml);
        Assert.Equal("a\nb\n\nc", YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
    }

    [Fact]
    public void StringStyle_Folded_WithAMoreIndentedLine_FallsBackToTheAutomaticStyle()
    {
        // Folding stops around a more indented line, which reads it back literally.
        var options = StringStyleOptions(ScalarStyle.Folded);
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "a\n  b" }, options);

        Assert.Equal("Text: \"a\\n  b\"\nOther: null\n", yaml);
        Assert.Equal("a\n  b", YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
    }

    [Fact]
    public void StringStyle_SingleQuoted_DoublesTheEmbeddedQuotes()
    {
        var options = StringStyleOptions(ScalarStyle.SingleQuoted);
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "it's #1", Other = "plain" }, options);

        Assert.Equal("Text: 'it''s #1'\nOther: 'plain'\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<TextContainer>(yaml, options);
        Assert.Equal("it's #1", roundTrip?.Text);
        Assert.Equal("plain", roundTrip?.Other);
    }

    [Fact]
    public void StringStyle_SingleQuoted_WithALineBreak_FallsBackToTheAutomaticStyle()
    {
        var options = StringStyleOptions(ScalarStyle.SingleQuoted);
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "a\nb" }, options);

        Assert.Equal("Text: \"a\\nb\"\nOther: null\n", yaml);
        Assert.Equal("a\nb", YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
    }

    [Fact]
    public void StringStyle_DoubleQuoted_QuotesEveryStringValue()
    {
        var options = StringStyleOptions(ScalarStyle.DoubleQuoted);
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "plain", Other = "a\nb" }, options);

        Assert.Equal("Text: \"plain\"\nOther: \"a\\nb\"\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<TextContainer>(yaml, options);
        Assert.Equal("plain", roundTrip?.Text);
        Assert.Equal("a\nb", roundTrip?.Other);
    }

    [Fact]
    public void StringStyle_Plain_LeavesAnAmbiguousScalarUnquoted()
    {
        var options = StringStyleOptions(ScalarStyle.Plain);
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "yes", Other = "a: b" }, options);

        // "a: b" cannot be written plain, so it keeps the automatic style.
        Assert.Equal("Text: yes\nOther: \"a: b\"\n", yaml);
    }

    [Fact]
    public void StringStyle_IsIgnoredForMappingKeysAndNonStringScalars()
    {
        var options = StringStyleOptions(ScalarStyle.Literal);
        var yaml = YamlSerializer.Serialize(new KeyedContainer { Map = new Dictionary<string, int>(StringComparer.Ordinal) { ["key"] = 1 }, Count = 2 }, options);

        Assert.Equal("Map:\n  key: 1\nCount: 2\n", yaml);
    }

    [Fact]
    public void StringStyle_WithoutIndentation_IsIgnoredForBlockStyles()
    {
        var options = StringStyleOptions(ScalarStyle.Literal) with { WriteIndented = false };
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "a\nb", Other = "c" }, options);

        Assert.Equal("{Text: \"a\\nb\", Other: c}\n", yaml);
        Assert.Equal("a\nb", YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
    }

    [Fact]
    public void StringStyleAttribute_OverridesTheStyleForTheAttributedMember()
    {
        var yaml = YamlSerializer.Serialize(new ScriptContainer { Script = "echo one\necho two", Description = "a\nb" });

        Assert.Equal("Script: |-\n  echo one\n  echo two\nDescription: \"a\\nb\"\nSteps: null\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<ScriptContainer>(yaml);
        Assert.Equal("echo one\necho two", roundTrip?.Script);
        Assert.Equal("a\nb", roundTrip?.Description);
    }

    [Fact]
    public void StringStyleAttribute_AppliesToTheStringsBelowTheAttributedMember()
    {
        var yaml = YamlSerializer.Serialize(new ScriptContainer { Steps = ["a\nb"] });

        Assert.Equal("Script: null\nDescription: null\nSteps:\n  - |-\n    a\n    b\n", yaml);
        Assert.Equal(["a\nb"], YamlSerializer.Deserialize<ScriptContainer>(yaml)?.Steps);
    }

    [Fact]
    public void PushStringStyle_RestoresThePreviousStyleWhenDisposed()
    {
        var writer = CreateWriter(new YamlSerializerOptions(), out var buffer);

        writer.WriteStartMapping();
        writer.WritePropertyName("a");
        using (writer.PushStringStyle(ScalarStyle.Literal))
        {
            writer.WriteString("x\ny");
        }

        writer.WritePropertyName("b");
        writer.WriteString("x\ny");
        writer.WriteEndMapping();

        Assert.Equal("a: |-\n  x\n  y\nb: \"x\\ny\"", buffer.ToString());
    }

    [Fact]
    public void FlowStyle_QuotesAQuestionMarkThatWouldEndThePlainScalar()
    {
        // Inside a flow collection a '?' ends a plain scalar wherever it appears, so it has to be quoted.
        var cases = new (string Value, string ExpectedYaml)[]
        {
            ("?b", "{Text: \"?b\", Other: null}\n"),
            ("a?", "{Text: \"a?\", Other: null}\n"),
            ("a?b", "{Text: \"a?b\", Other: null}\n"),
            ("a? b", "{Text: \"a? b\", Other: null}\n"),
        };

        foreach (var @case in cases)
        {
            var options = new YamlSerializerOptions { WriteIndented = false };
            var yaml = YamlSerializer.Serialize(new TextContainer { Text = @case.Value }, options);

            Assert.Equal(@case.ExpectedYaml, yaml);
            Assert.Equal(@case.Value, YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
        }
    }

    [Fact]
    public void BlockStyle_LeavesAQuestionMarkUnquoted()
    {
        // A '?' only ends a plain scalar in the flow context, so block output keeps it plain.
        var yaml = YamlSerializer.Serialize(new TextContainer { Text = "why?", Other = "a?b" });

        Assert.Equal("Text: why?\nOther: a?b\n", yaml);

        var roundTrip = YamlSerializer.Deserialize<TextContainer>(yaml);
        Assert.Equal("why?", roundTrip?.Text);
        Assert.Equal("a?b", roundTrip?.Other);
    }

    [Fact]
    public void FlowStyle_QuotesTheOtherFlowIndicators()
    {
        var options = new YamlSerializerOptions { WriteIndented = false };
        var value = new StringsContainer { Items = ["a,b", "a[b", "a]b", "a{b", "a}b", "a?b"] };
        var yaml = YamlSerializer.Serialize(value, options);

        Assert.Equal("{Items: [\"a,b\", \"a[b\", \"a]b\", \"a{b\", \"a}b\", \"a?b\"], Nested: null}\n", yaml);
        Assert.Equal(value.Items, YamlSerializer.Deserialize<StringsContainer>(yaml, options)?.Items);
    }

    [Fact]
    public void UnicodeLineSeparators_AreEscapedAndRoundTrip()
    {
        // U+0085, U+2028, and U+2029 are line breaks to a YAML reader even though they are not control characters.
        var cases = new (string Value, string ExpectedYaml)[]
        {
            ("a\u0085b", "Text: \"a\\u0085b\"\nOther: null\n"),
            ("a\u2028b", "Text: \"a\\u2028b\"\nOther: null\n"),
            ("a\u2029b", "Text: \"a\\u2029b\"\nOther: null\n"),
        };

        foreach (var @case in cases)
        {
            var yaml = YamlSerializer.Serialize(new TextContainer { Text = @case.Value });

            Assert.Equal(@case.ExpectedYaml, yaml);
            Assert.Equal(@case.Value, YamlSerializer.Deserialize<TextContainer>(yaml)?.Text);
        }
    }

    [Fact]
    public void UnicodeLineSeparators_AreRejectedByTheVerbatimStringStyles()
    {
        // A block, single-quoted, or plain scalar writes its content verbatim, so these would come back as breaks.
        foreach (var style in new[] { ScalarStyle.Literal, ScalarStyle.Folded, ScalarStyle.SingleQuoted, ScalarStyle.Plain })
        {
            foreach (var value in new[] { "a\u0085b", "a\u2028b", "a\u2029b" })
            {
                var options = StringStyleOptions(style);
                var yaml = YamlSerializer.Serialize(new TextContainer { Text = value }, options);

                Assert.StartsWith("Text: \"", yaml);
                Assert.Equal(value, YamlSerializer.Deserialize<TextContainer>(yaml, options)?.Text);
            }
        }
    }

    private static YamlSerializerOptions StringStyleOptions(ScalarStyle style)
        => new() { ScalarStylePreferences = new YamlScalarStylePreferences { StringStyle = style } };

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

    private sealed class StringsContainer
    {
        public List<string>? Items { get; set; }

        public TextContainer? Nested { get; set; }
    }

    private sealed class KeyedContainer
    {
        public Dictionary<string, int>? Map { get; set; }

        public int Count { get; set; }
    }

    private sealed class ScriptContainer
    {
        [YamlStringStyle(ScalarStyle.Literal)]
        public string? Script { get; set; }

        public string? Description { get; set; }

        [YamlStringStyle(ScalarStyle.Literal)]
        public List<string>? Steps { get; set; }
    }

    private sealed class TagsContainer
    {
        public List<TaggedItem>? Items { get; set; }
    }

    private sealed class TaggedItem
    {
        public string? Name { get; set; }

        public List<string>? Tags { get; set; }
    }
}
