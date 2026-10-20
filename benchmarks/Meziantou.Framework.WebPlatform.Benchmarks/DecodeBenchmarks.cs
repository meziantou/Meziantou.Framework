using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>Decodes the bytes of a document (https://encoding.spec.whatwg.org/#decode), at once and in chunks as the HTML parser does.</summary>
[MemoryDiagnoser]
public class DecodeBenchmarks
{
    private byte[] _bytes = [];
    private WebEncodingId _encodingId;

    [Params("wikipedia", "html-spec")]
    public string Document { get; set; } = "wikipedia";

    [Params("utf-8", "windows-1252")]
    public string Encoding { get; set; } = "utf-8";

    [GlobalSetup]
    public void Setup()
    {
        _bytes = System.Text.Encoding.UTF8.GetBytes(BenchmarkData.Get(Document));
        _encodingId = Encoding switch
        {
            "utf-8" => WebEncodingId.Utf8,
            "windows-1252" => WebEncodingId.Windows1252,
            _ => throw new InvalidOperationException("Unknown encoding"),
        };
    }

    [Benchmark]
    public string DecodeToString() => WebEncodingRegistry.CreateDecoder(_encodingId).DecodeToString(_bytes);

    [Benchmark]
    public int DecodeChunks()
    {
        const int ChunkSize = 16 * 1024;
        var decoder = WebEncodingRegistry.CreateDecoder(_encodingId);
        var length = 0;
        for (var offset = 0; offset < _bytes.Length; offset += ChunkSize)
        {
            var builder = new StringBuilder();
            decoder.DecodeTo(_bytes.AsSpan(offset, Math.Min(ChunkSize, _bytes.Length - offset)), builder, flush: offset + ChunkSize >= _bytes.Length);
            length += builder.Length;
        }

        return length;
    }
}
