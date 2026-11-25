using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Compression.Benchmarks;

[MemoryDiagnoser]
public class SevenZipBenchmark : IDisposable
{
    private byte[] _data = [];
    private byte[] _compressed = [];
    private MemoryStream _output = new();

    [Params(DataKind.Text, DataKind.Binary)]
    public DataKind Kind { get; set; }

    [Params(SevenZipCompressionMethod.Ppmd, SevenZipCompressionMethod.Lzma2)]
    public SevenZipCompressionMethod Method { get; set; }

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
        using var archive = new SevenZipArchive(new MemoryStream(_compressed));
        using var stream = archive.Entries[0].Open();
        _output.Position = 0;
        stream.CopyTo(_output);
        return _output.Position;
    }

    [Benchmark]
    public int Compress() => CompressCore().Length;

    private byte[] CompressCore()
    {
        var output = new MemoryStream();
        using (var archive = new SevenZipArchive(output, SevenZipArchiveMode.Create, leaveOpen: true, readerOptions: null, new SevenZipCompressionOptions { Method = Method, Filter = SevenZipFilter.None }))
        {
            using var stream = archive.CreateEntry("data.bin").Open();
            stream.Write(_data);
        }

        return output.ToArray();
    }

    public void Dispose() => _output.Dispose();
}
