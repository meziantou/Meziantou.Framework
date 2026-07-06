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
        Assert.Equal("\"Test\\xD802\\xDD05Yo♥\"", result);
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
}
