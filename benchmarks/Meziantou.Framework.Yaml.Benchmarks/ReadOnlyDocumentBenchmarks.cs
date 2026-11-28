using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Nodes;
using SY = System.Text.Yaml;

namespace Meziantou.Framework.Yaml.Benchmarks;

/// <summary>
/// Parses the documents of Styml's benchmark corpus into a read-only document and writes them back as UTF-8, compared
/// with Styml's <c>YamlStreamDocument</c> and <c>Utf8YamlWriter</c>, the document object model and the event emitter.
/// </summary>
[MemoryDiagnoser]
public class ReadOnlyDocumentBenchmarks
{
    private byte[] _utf8 = null!;
    private string _text = null!;
    private YamlDocument _document = null!;
    private SY.YamlDocument _stymlDocument = null!;
    private List<ParsingEvent> _events = null!;
    private ArrayBufferWriter<byte> _buffer = null!;

    [Params("k8s-m", "openapi-m", "records-m")]
    public string Document { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        using var stream = typeof(ReadOnlyDocumentBenchmarks).Assembly.GetManifestResourceStream("corpus." + Document + ".yaml")!;
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        _utf8 = memoryStream.ToArray();
        _text = Encoding.UTF8.GetString(_utf8);
        _document = YamlDocument.Parse(_utf8);
        _stymlDocument = SY.YamlDocument.Parse(_utf8, default);
        _buffer = new ArrayBufferWriter<byte>(_utf8.Length * 2);

        _events = [];
        var parser = Parser.CreateParser(new StringReader(_text));
        while (parser.MoveNext())
        {
            _events.Add(parser.Current!);
        }

        // Every writer reads back as the same document
        Console.WriteLine(FormattableString.Invariant($"// {Document}: {_utf8.Length} bytes, Utf8YamlWriter {Utf8YamlWriter_WriteTo()} bytes, Styml {Styml_WriteTo()} bytes, Emitter {Emitter_WriteEvents()} chars"));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _document.Dispose();
        _stymlDocument.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int ReadOnlyDocument_Parse()
    {
        using var document = YamlDocument.Parse(_utf8);
        return document.DocumentCount;
    }

    [Benchmark]
    public int Styml_YamlDocument_Parse()
    {
        using var document = SY.YamlDocument.Parse(_utf8, default);
        return (int)document.RootElement.ValueKind;
    }

    [Benchmark]
    public int YamlStream_Load() => YamlStream.Load(new StringReader(_text)).Count;

    [Benchmark]
    public int Utf8YamlWriter_WriteTo()
    {
        _buffer.ResetWrittenCount();
        using (var writer = new Utf8YamlWriter(_buffer))
        {
            _document.WriteTo(writer);
        }

        return _buffer.WrittenCount;
    }

    [Benchmark]
    public int Styml_WriteTo()
    {
        _buffer.ResetWrittenCount();
        using (var writer = new SY.Utf8YamlWriter(_buffer, default))
        {
            _stymlDocument.WriteTo(writer);
        }

        return _buffer.WrittenCount;
    }

    // The events are parsed once: only the writing is measured
    [Benchmark]
    public int Emitter_WriteEvents()
    {
        var writer = new StringWriter(new StringBuilder(_text.Length * 2), System.Globalization.CultureInfo.InvariantCulture);
        var emitter = new Emitter(writer);
        foreach (var parsingEvent in _events)
        {
            emitter.Emit(parsingEvent);
        }

        return writer.GetStringBuilder().Length;
    }
}
