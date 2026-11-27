using System.IO.Compression;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Compression.Benchmarks;

[MemoryDiagnoser]
public class XzBenchmark : IDisposable
{
    private byte[] _data = [];
    private byte[] _compressed = [];
    private MemoryStream _output = new();

    [Params(DataKind.Text, DataKind.Binary)]
    public DataKind Kind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = BenchmarkData.Create(Kind);
        _compressed = Compress(6);
        _output = new MemoryStream(_data.Length * 2);
    }

    [Benchmark]
    public long Decompress()
    {
        using var stream = new XzStream(new MemoryStream(_compressed), CompressionMode.Decompress);
        _output.Position = 0;
        stream.CopyTo(_output);
        return _output.Position;
    }

    [Benchmark]
    public int CompressPreset1() => Compress(1).Length;

    [Benchmark]
    public int CompressPreset6() => Compress(6).Length;

    private byte[] Compress(int preset)
    {
        var output = new MemoryStream();
        using (var stream = new XzStream(output, new XzCompressionOptions { Preset = preset }, leaveOpen: true))
        {
            stream.Write(_data);
        }

        return output.ToArray();
    }

    public void Dispose() => _output.Dispose();
}
