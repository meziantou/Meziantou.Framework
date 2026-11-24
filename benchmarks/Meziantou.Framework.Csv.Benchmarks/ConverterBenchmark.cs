using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Csv.Serialization;
using Meziantou.Framework.Csv.Serialization.Metadata;

namespace Meziantou.Framework.Csv.Benchmarks;

/// <summary>Reads the 10,000 values of each column of <see cref="NumericRow"/> with the default converter of its type.</summary>
public class ConverterBenchmark
{
    private string[][] _columns = [];
    private CsvConverterContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        var text = BenchmarkData.CreateNumericText(10_000);
        var rows = new List<string[]>();
        using var reader = CsvReader.CreateFromString(text, new CsvReaderOptions { HasHeaderRow = true });
        while (reader.Read())
        {
            var r = reader.Current;
            var values = new string[r.ColumnCount];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = r[i].ToString();
            }

            rows.Add(values);
        }

        _columns = [.. Enumerable.Range(0, 7).Select(c => rows.Select(r => r[c]).ToArray())];
        _context = new CsvConverterContext(CsvSerializerOptions.Default);
    }

    [Benchmark]
    public long ReadInt32() => Run<int>(0, static v => v);

    [Benchmark]
    public long ReadInt64() => Run<long>(1, static v => v);

    [Benchmark]
    public long ReadDouble() => Run<double>(2, static v => (long)v);

    [Benchmark]
    public long ReadDoubleShort() => Run<double>(3, static v => (long)v);

    [Benchmark]
    public long ReadDecimal() => Run<decimal>(4, static v => (long)v);

    [Benchmark]
    public long ReadDateTime() => Run<DateTime>(5, static v => v.Ticks);

    [Benchmark]
    public long ReadBoolean() => Run<bool>(6, static v => v ? 1 : 0);

    private long Run<T>(int column, Func<T, long> toLong)
    {
        var converter = CsvMetadataServices.GetConverter<T>(CsvSerializerOptions.Default);
        long sum = 0;
        foreach (var value in _columns[column])
        {
            sum += toLong(converter.Read(value, _context));
        }

        return sum;
    }
}
