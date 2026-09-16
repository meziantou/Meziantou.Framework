using System.Diagnostics;
using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Model;

namespace Meziantou.Framework.Yaml.Tests;

public class EmitterTests : YamlTest
{
    public EmitterTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
    [Fact]
    public void EmitExample1()
    {
        ParseAndEmit("test1.yaml");
    }

    [Fact]
    public void EmitExample2()
    {
        ParseAndEmit("test2.yaml");
    }

    [Fact]
    public void EmitExample3()
    {
        ParseAndEmit("test3.yaml");
    }

    [Fact]
    public void EmitExample4()
    {
        ParseAndEmit("test4.yaml");
    }

    [Fact]
    public void EmitExample5()
    {
        ParseAndEmit("test5.yaml");
    }

    [Fact]
    public void EmitExample6()
    {
        ParseAndEmit("test6.yaml");
    }

    [Fact]
    public void EmitExample7()
    {
        ParseAndEmit("test7.yaml");
    }

    [Fact]
    public void EmitExample8()
    {
        ParseAndEmit("test8.yaml");
    }

    [Fact]
    public void EmitExample9()
    {
        ParseAndEmit("test9.yaml");
    }

    [Fact]
    public void EmitExample10()
    {
        ParseAndEmit("test10.yaml");
    }

    [Fact]
    public void EmitExample11()
    {
        ParseAndEmit("test11.yaml");
    }

    [Fact]
    public void EmitExample12()
    {
        ParseAndEmit("test12.yaml");
    }

    [Fact]
    public void EmitExample13()
    {
        ParseAndEmit("test13.yaml");
    }

    [Fact]
    public void EmitExample14()
    {
        ParseAndEmit("test14.yaml");
    }

    [Fact]
    public void EmitUnicode()
    {
        var encoding = Encoding.GetEncoding(28595); // Cyrillic
        var stream = new MemoryStream();
        var input = "Гранит дзень";
        using (var writer = new StreamWriter(stream, encoding))
        {
            var emitter = new Emitter(writer);
            emitter.Emit(new StreamStart());
            emitter.Emit(new DocumentStart(null, null, true));
            emitter.Emit(new Scalar(input, ScalarStyle.SingleQuoted));
            emitter.Emit(new DocumentEnd(true));
        }
        var result = encoding.GetString(stream.ToArray()).Trim();
        Assert.Equal("'" + input + "'", result);
    }

    [Fact]
    public void EmitUnicodeEscapes()
    {
        var encoding = new UTF8Encoding(false);
        var stream = new MemoryStream();
        var input = "Test\U00010905Yo♥";
        using (var writer = new StreamWriter(stream, encoding))
        {
            var emitter = new Emitter(writer);
            emitter.Emit(new StreamStart());
            emitter.Emit(new DocumentStart(null, null, true));
            emitter.Emit(new Scalar(input, ScalarStyle.DoubleQuoted));
            emitter.Emit(new DocumentEnd(true));
        }
        var result = encoding.GetString(stream.ToArray()).Trim();
        Assert.Equal("\"Test\\U00010905Yo♥\"", result);
    }

    [Theory]
    [InlineData("\U0001F600")]
    [InlineData("\U00020000")]
    [InlineData("\U00010905")]
    [InlineData("a\U0001F600b")]
    [InlineData("\U0001F600\U0001F601")]
    [InlineData("♥\U00010905中")]
    public void EmitNonBmpCharacters_RoundTrips(string input)
    {
        var yaml = EmitDoubleQuotedScalar(input);

        Assert.Equal(input, ParseSingleScalarValue(yaml));
    }

    [Fact]
    public void EmitNonBmpCharacter_UsesEightDigitEscape()
    {
        Assert.Equal("\"\\U0001F600\"", EmitDoubleQuotedScalar("\U0001F600").Trim());
    }

    [Fact]
    public void EmitBmpCharacterAboveShortMaxValue_UsesFourDigitEscape()
    {
        // U+FFFE is above short.MaxValue and is not printable, so it must use the 4-digit escape.
        Assert.Equal("\"\\uFFFE\"", EmitDoubleQuotedScalar("\uFFFE").Trim());
    }

    private static string EmitDoubleQuotedScalar(string value)
    {
        using var output = new StringWriter();
        var emitter = new Emitter(output);
        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart(null, null, true));
        emitter.Emit(new Scalar(value, ScalarStyle.DoubleQuoted));
        emitter.Emit(new DocumentEnd(true));
        emitter.Emit(new StreamEnd());
        return output.ToString();
    }

    private static string? ParseSingleScalarValue(string yaml)
    {
        var parser = Parser.CreateParser(new StringReader(yaml));
        while (parser.MoveNext())
        {
            if (parser.Current is Scalar scalar)
                return scalar.Value;
        }

        return null;
    }

    private static void ParseAndEmit(string name)
    {
        using var reader = YamlFile(name);
        var testText = reader.ReadToEnd();

        using var output = new StringWriter();
        var parser = Parser.CreateParser(new StringReader(testText));
        var emitter = new Emitter(output, 2, int.MaxValue, false);
        Dump.WriteLine("= Parse and emit yaml file [" + name + "] =");
        while (parser.MoveNext())
        {
            Debug.Assert(parser.Current != null);
            Dump.WriteLine(parser.Current);
            emitter.Emit(parser.Current);
        }
        Dump.WriteLine();

        Dump.WriteLine("= Original =");
        Dump.WriteLine(testText);
        Dump.WriteLine();

        Dump.WriteLine("= Result =");
        Dump.WriteLine(output);
        Dump.WriteLine();
    }

    private static string EmitScalar(Scalar scalar)
    {
        return Emit(
            new SequenceStart(null, null, false, YamlStyle.Block),
            scalar,
            new SequenceEnd()
            );
    }

    private static string Emit(params ParsingEvent[] events)
    {
        using var buffer = new StringWriter();
        var emitter = new Emitter(buffer);
        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart(null, null, true));

        foreach (var evt in events)
        {
            emitter.Emit(evt);
        }

        emitter.Emit(new DocumentEnd(true));
        emitter.Emit(new StreamEnd());

        return buffer.ToString();
    }

    [Theory]
    [InlineData("LF hello\nworld")]
    [InlineData("CRLF hello\r\nworld")]
    public void FoldedStyleDoesNotLooseCharacters(string text)
    {
        var yaml = EmitScalar(new Scalar(null, null, text, ScalarStyle.Folded, true, false));
        Dump.WriteLine(yaml);
        Assert.Contains("world", yaml);
    }

    // We are disabling this and want to keep the \n in the output. It is better to have folded > ?
    //[Fact]
    //public void FoldedStyleIsSelectedWhenNewLinesAreFoundInLiteral()
    //{
    //    var yaml = EmitScalar(new Scalar(null, null, "hello\nworld", ScalarStyle.Any, true, false));
    //    Dump.WriteLine(yaml);
    //    Assert.True(yaml.Contains(">"));
    //}

    [Fact]
    public void FoldedStyleDoesNotGenerateExtraLineBreaks()
    {
        var yaml = EmitScalar(new Scalar(null, null, "hello\nworld", ScalarStyle.Folded, true, false));
        Dump.WriteLine(yaml);

        var stream = YamlStream.Load(new StringReader(yaml));
        var sequence = (YamlSequence)stream[0].Contents!;
        var scalar = (YamlValue)sequence[0];

        Assert.Equal("hello\nworld", scalar.Value);
    }

    [Fact]
    public void FoldedStyleDoesNotCollapseLineBreaks()
    {
        var yaml = EmitScalar(new Scalar(null, null, ">+\n", ScalarStyle.Folded, true, false));
        Dump.WriteLine("${0}$", yaml);

        var stream = YamlStream.Load(new StringReader(yaml));
        var sequence = (YamlSequence)stream[0].Contents!;
        var scalar = (YamlValue)sequence[0];

        Assert.Equal(">+\n", scalar.Value);
    }

    [Fact]
    public void FoldedStylePreservesNewLines()
    {
        var input = "id: 0\nPayload:\n  X: 5\n  Y: 6\n";

        var yaml = Emit(
            new MappingStart(),
            new Scalar("Payload"),
            new Scalar(null, null, input, ScalarStyle.Folded, true, false),
            new MappingEnd()
            );
        Dump.WriteLine(yaml);

        var stream = YamlStream.Load(new StringReader(yaml));

        var mapping = (YamlMapping)stream[0].Contents!;
        var value = (YamlValue)mapping[0].Value!;

        var output = value.Value;
        Dump.WriteLine(output);
        Assert.Equal(input, output);
    }

    [Fact]
    public void FoldedScalarWithMultipleWordsPreservesLineBreaks()
    {
        // The real issue is not that "a folded\nscalar" should become "a folded scalar"
        // in terms of content (that's actually correct YAML behavior)
        // The issue is that when emitting a scalar with newlines as a folded scalar,
        // it should preserve the newlines in the YAML structure

        var input = "a folded\nscalar";

        // When we emit a scalar with embedded newlines as a folded scalar,
        // it should be emitted as:
        // >-
        //   a folded
        //   scalar
        // NOT as:
        // >-
        //   a folded scalar

        var yaml = EmitScalar(new Scalar(null, null, input, ScalarStyle.Folded, true, false));
        Console.WriteLine("Emitted YAML:");
        Console.WriteLine(yaml);

        // The emitted YAML should contain the folded scalar structure
        Assert.Contains(">-", yaml, message: "Should emit as folded scalar");
        Assert.Contains("a folded", yaml, message: "Should contain the first part");
        Assert.Contains("scalar", yaml, message: "Should contain the second part");

        // Parse it back and verify the content is preserved
        var stream = YamlStream.Load(new StringReader(yaml));
        var sequence = (YamlSequence)stream[0].Contents!;
        var scalar = (YamlValue)sequence[0];

        Console.WriteLine($"Original: '{input}'");
        Console.WriteLine($"Round-trip result: '{scalar.Value}'");

        // This should pass - the content should be preserved
        Assert.Equal(input, scalar.Value, "Folded scalar content should be preserved during round-trip");
    }

    [Fact]
    public void WriteTo_TagWithoutMatchingDirective_IsWrittenVerbatim()
    {
        var yaml = SaveAndReload("--- !<tag:example.com,2000:app/invoice> x\n", out var reloaded);

        Assert.Equal("--- !<tag:example.com,2000:app/invoice> x\n...\n", yaml);
        Assert.Equal("tag:example.com,2000:app/invoice", ((YamlValue)reloaded[0].Contents!).Tag);
    }

    [Fact]
    public void WriteTo_NonSpecificTag_IsKept()
    {
        var yaml = SaveAndReload("- ! 12\n- 12\n", out var reloaded);

        Assert.Equal("- ! 12\n- 12\n", yaml);
        Assert.Equal("!", ((YamlValue)((YamlSequence)reloaded[0].Contents!)[0]).Tag);
    }

    [Fact]
    public void WriteTo_DefaultTagDirectives_AreNotWritten()
    {
        var yaml = SaveAndReload("a: 1\n", out _);

        Assert.Equal("a: 1\n", yaml);
    }

    [Theory]
    [InlineData("a: 1\n---\nb: 2\n", "a: 1\n---\nb: 2\n")]
    [InlineData("a: 1\n...\n%TAG !e! tag:example.com,2000:\n--- !e!x b\n", "a: 1\n...\n%TAG !e! tag:example.com,2000:\n--- !e!x b\n...\n")]
    [InlineData("a\n...\n%YAML 1.2\n---\nb\n", "a\n...\n%YAML 1.2\n--- b\n...\n")]
    public void WriteTo_MultipleDocuments_RoundTrips(string input, string expected)
    {
        var yaml = SaveAndReload(input, out var reloaded);

        Assert.Equal(expected, yaml);
        Assert.HasCount(2, reloaded);
    }

    [Fact]
    public void WriteTo_SingleQuotedScalarEndingWithLineBreak_IndentsClosingQuote()
    {
        SaveAndReload("a: '\n\n  '\nb: 'x\n\n  '\n", out var reloaded);

        var mapping = (YamlMapping)reloaded[0].Contents!;
        Assert.Equal("\n", ((YamlValue)mapping["a"]!).Value);
        Assert.Equal("x\n", ((YamlValue)mapping["b"]!).Value);
    }

    [Fact]
    public void WriteTo_EmptyStream_WritesNothing()
    {
        Assert.Equal(string.Empty, Save(new YamlStream()));
        Assert.Equal(string.Empty, new YamlStream().ToString());
    }

    [Fact]
    public void WriteTo_DocumentWithoutContents_WritesEmptyDocument()
    {
        var stream = new YamlStream { new YamlDocument() };

        var reloaded = YamlStream.Load(new StringReader(Save(stream)));
        Assert.HasCount(1, reloaded);
        Assert.Equal(string.Empty, ((YamlValue)reloaded[0].Contents!).Value);
    }

    [Fact]
    public void WriteTo_SuppressDocumentTags_DoesNotRemoveDirectivesFromTheModel()
    {
        var stream = YamlStream.Load(new StringReader("%TAG !e! tag:example.com,2000:\n--- !e!foo bar\n"));

        Assert.Equal("--- !<tag:example.com,2000:foo> bar\n...", stream.ToString().ReplaceLineEndings("\n"));
        Assert.Contains(stream[0].DocumentStart.Tags!, tag => tag.Handle == "!e!");
        Assert.Equal("%TAG !e! tag:example.com,2000:\n--- !e!foo bar\n...\n", Save(stream));
    }

    private static string Save(YamlNode node)
    {
        using var writer = new StringWriter { NewLine = "\n" };
        node.WriteTo(writer);
        return writer.ToString();
    }

    private static string SaveAndReload(string yaml, out YamlStream reloaded)
    {
        var output = Save(YamlStream.Load(new StringReader(yaml)));
        reloaded = YamlStream.Load(new StringReader(output));
        return output;
    }
}
