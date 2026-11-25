using System.IO.Compression;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Compression.Benchmarks;

[MemoryDiagnoser]
public class BZip2Benchmark : IDisposable
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
        _compressed = CompressCore();
        _output = new MemoryStream(_data.Length * 2);
    }

    [Benchmark]
    public long Decompress()
    {
        using var stream = new BZip2Stream(new MemoryStream(_compressed), CompressionMode.Decompress);
        _output.Position = 0;
        stream.CopyTo(_output);
        return _output.Position;
    }

    [Benchmark]
    public int Compress() => CompressCore().Length;

    private byte[] CompressCore()
    {
        var output = new MemoryStream();
        using (var stream = new BZip2Stream(output, CompressionMode.Compress, leaveOpen: true))
        {
            stream.Write(_data);
        }

        return output.ToArray();
    }

    public void Dispose() => _output.Dispose();
}
