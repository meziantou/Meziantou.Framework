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
    private string _numericText = "";
    private byte[] _numericUtf8 = [];

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rows = BenchmarkData.CreateRows(Rows);
        _text = BenchmarkData.CreateText(Rows);
        _utf8 = Encoding.UTF8.GetBytes(_text);
        _numericText = BenchmarkData.CreateNumericText(Rows);
        _numericUtf8 = Encoding.UTF8.GetBytes(_numericText);
    }

    [Benchmark(Baseline = true)]
    public int Deserialize_Generated() => CsvSerializer.Deserialize(_text, BenchmarkCsvContext.Default.BenchmarkRow).Count;

    [Benchmark]
    public int Deserialize_Generated_Utf8() => CsvSerializer.Deserialize(_utf8.AsMemory(), BenchmarkCsvContext.Default.BenchmarkRow).Count;

    [Benchmark]
    public int Deserialize_Reflection() => CsvSerializer.Deserialize<BenchmarkRow>(_text).Count;

    /// <summary>Reads records without text values: the conversion of the numbers is not hidden by the allocation of strings.</summary>
    [Benchmark]
    public int DeserializeNumeric_Generated() => CsvSerializer.Deserialize(_numericText, BenchmarkCsvContext.Default.NumericRow).Count;

    [Benchmark]
    public int DeserializeNumeric_Generated_Utf8() => CsvSerializer.Deserialize(_numericUtf8.AsMemory(), BenchmarkCsvContext.Default.NumericRow).Count;

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
