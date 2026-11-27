using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Csv.Serialization;

namespace Meziantou.Framework.Csv.Benchmarks;

/// <summary>Reads typed records with the conversion of the records on the calling thread (1) or on the thread pool (more than 1, or -1 for every processor).</summary>
/// <remarks>Run it with the server garbage collector too (<c>DOTNET_gcServer=1</c>): the allocation of the strings limits the parallel conversion of text records.</remarks>
[MemoryDiagnoser]
public class ParallelDeserializeBenchmark
{
    private string _text = "";
    private byte[] _utf8 = [];
    private string _numericText = "";
    private byte[] _numericUtf8 = [];
    private BenchmarkCsvContext _context = BenchmarkCsvContext.Default;

    [Params(1_000, 100_000)]
    public int Rows { get; set; }

    [Params(1, 4, -1)]
    public int DegreeOfParallelism { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _text = BenchmarkData.CreateText(Rows);
        _utf8 = Encoding.UTF8.GetBytes(_text);
        _numericText = BenchmarkData.CreateNumericText(Rows);
        _numericUtf8 = Encoding.UTF8.GetBytes(_numericText);
        _context = new BenchmarkCsvContext(new CsvSerializerOptions { MaxDegreeOfParallelism = DegreeOfParallelism });
    }

    [Benchmark]
    public int Deserialize() => CsvSerializer.Deserialize(_text, _context.BenchmarkRow).Count;

    [Benchmark]
    public int Deserialize_Utf8() => CsvSerializer.Deserialize(_utf8.AsMemory(), _context.BenchmarkRow).Count;

    [Benchmark]
    public int DeserializeNumeric() => CsvSerializer.Deserialize(_numericText, _context.NumericRow).Count;

    [Benchmark]
    public int DeserializeNumeric_Utf8() => CsvSerializer.Deserialize(_numericUtf8.AsMemory(), _context.NumericRow).Count;

    [Benchmark]
    public long DeserializeNumeric_Stream()
    {
        using var stream = new MemoryStream(_numericUtf8, writable: false);
        long count = 0;
        foreach (var row in CsvSerializer.DeserializeEnumerable(stream, _context.NumericRow))
        {
            count += row.Count;
        }

        return count;
    }
}
