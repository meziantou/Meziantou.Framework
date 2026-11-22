using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Csv.Serialization;

namespace Meziantou.Framework.Csv.Benchmarks;

/// <summary>Reads and writes typed records, with the generated metadata and with reflection.</summary>
[MemoryDiagnoser]
public class SerializerBenchmark
{
    private string _text = "";
    private byte[] _utf8 = [];
    private List<BenchmarkRow> _rows = [];

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rows = BenchmarkData.CreateRows(Rows);
        _text = BenchmarkData.CreateText(Rows);
        _utf8 = Encoding.UTF8.GetBytes(_text);
    }

    [Benchmark(Baseline = true)]
    public int Deserialize_Generated() => CsvSerializer.Deserialize(_text, BenchmarkCsvContext.Default.BenchmarkRow).Count;

    [Benchmark]
    public int Deserialize_Generated_Utf8() => CsvSerializer.Deserialize(_utf8.AsMemory(), BenchmarkCsvContext.Default.BenchmarkRow).Count;

    [Benchmark]
    public int Deserialize_Reflection() => CsvSerializer.Deserialize<BenchmarkRow>(_text).Count;

    [Benchmark]
    public int Serialize_Generated() => CsvSerializer.Serialize(_rows, BenchmarkCsvContext.Default.BenchmarkRow).Length;

    [Benchmark]
    public long Serialize_Generated_Utf8()
    {
        using var stream = new MemoryStream();
        CsvSerializer.Serialize(stream, _rows, BenchmarkCsvContext.Default.BenchmarkRow);
        return stream.Length;
    }

    [Benchmark]
    public int Serialize_Reflection() => CsvSerializer.Serialize(_rows).Length;
}
