using System.Text;
using BenchmarkDotNet.Attributes;
using SY = System.Text.Yaml;

namespace Meziantou.Framework.Yaml.Benchmarks;

/// <summary>
/// Reads every token of the documents of Styml's benchmark corpus with the prototype <see cref="Utf8YamlReader"/>, the
/// existing <see cref="Parser"/> and Styml's <c>Utf8YamlReader</c>.
/// </summary>
[MemoryDiagnoser]
public class ReaderBenchmarks
{
    private byte[] _utf8 = null!;
    private string _text = null!;
    private byte[] _decodeBuffer = null!;

    [Params("k8s-s", "workflow-m", "compose-m", "k8s-m", "openapi-m", "records-m")]
    public string Document { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        using var stream = typeof(ReaderBenchmarks).Assembly.GetManifestResourceStream("corpus." + Document + ".yaml")!;
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        _utf8 = memoryStream.ToArray();
        _text = Encoding.UTF8.GetString(_utf8);
        _decodeBuffer = new byte[(_utf8.Length * 2) + 16];

        // The readers do not split the document into the same tokens
        Console.WriteLine(FormattableString.Invariant($"// Tokens of {Document}: Utf8YamlReader {Utf8YamlReader_Tokens()}, Parser {Parser_Events()}, Styml {Styml_Tokens()}"));
    }

    [Benchmark(Baseline = true)]
    public int Utf8YamlReader_Tokens()
    {
        var reader = new Utf8YamlReader(_utf8);
        var count = 0;
        while (reader.Read())
        {
            count++;
        }

        return count;
    }

    // The prototype does not validate the UTF-8 input: this row adds the cost of a validation pass
    [Benchmark]
    public int Utf8YamlReader_ValidateUtf8AndTokens()
    {
        if (!System.Text.Unicode.Utf8.IsValid(_utf8))
            throw new InvalidOperationException("Invalid UTF-8");

        var reader = new Utf8YamlReader(_utf8);
        var count = 0;
        while (reader.Read())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int Utf8YamlReader_TokensAndDecodeToBuffer()
    {
        var reader = new Utf8YamlReader(_utf8);
        var length = 0;
        while (reader.Read())
        {
            if (reader.TokenKind is Utf8YamlTokenKind.Scalar)
            {
                length += reader.CopyValue(_decodeBuffer);
            }
        }

        return length;
    }

    [Benchmark]
    public int Utf8YamlReader_TokensAndGetString()
    {
        var reader = new Utf8YamlReader(_utf8);
        var length = 0;
        while (reader.Read())
        {
            if (reader.TokenKind is Utf8YamlTokenKind.Scalar)
            {
                length += reader.GetString().Length;
            }
        }

        return length;
    }

    [Benchmark]
    public int Parser_Events()
    {
        var parser = Parser.CreateParser(new StringReader(_text));
        var count = 0;
        while (parser.MoveNext())
        {
            count++;
        }

        return count;
    }

    // The scanner and parser that Parser.CreateParser used before Utf8YamlParser
    [Benchmark]
    public int OldParser_Events()
    {
        var parser = new Parser<StringLookAheadBuffer>(new StringLookAheadBuffer(_text));
        var count = 0;
        while (parser.MoveNext())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int Styml_Tokens()
    {
        var reader = new SY.Utf8YamlReader(_utf8);
        var count = 0;
        while (reader.Read())
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int Styml_TokensAndGetString()
    {
        var reader = new SY.Utf8YamlReader(_utf8);
        var length = 0;
        while (reader.Read())
        {
            if (reader.TokenType is SY.YamlTokenType.String or SY.YamlTokenType.PropertyName)
            {
                length += reader.GetString()!.Length;
            }
        }

        return length;
    }
}
