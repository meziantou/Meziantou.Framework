using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Csv.Serialization;
using Meziantou.Framework.Csv.Serialization.Metadata;

namespace Meziantou.Framework.Csv.Benchmarks;

/// <summary>Reads the 10,000 values of each column of <see cref="NumericRow"/> with the default converter of its type, and formats some of them.</summary>
public class ConverterBenchmark
{
    private string[][] _columns = [];
    private CsvConverterContext _context = null!;
    private double[] _doubles = [];
    private double[] _shortDoubles = [];
    private double[] _exponentDoubles = [];
    private float[] _singles = [];
    private readonly char[] _buffer = new char[64];

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

        // The values of the Value column in the round-trip format of a float, which has up to 9 digits
        _columns = [.. _columns, [.. _columns[2].Select(value => ((float)double.Parse(value, CultureInfo.InvariantCulture)).ToString("R", CultureInfo.InvariantCulture))]];
        _context = new CsvConverterContext(CsvSerializerOptions.Default);
        _doubles = [.. _columns[2].Select(value => double.Parse(value, CultureInfo.InvariantCulture))];
        _shortDoubles = [.. _columns[3].Select(value => double.Parse(value, CultureInfo.InvariantCulture))];

        // The same values from 10^-9 to 10^-6 and from 10^20 to 10^23, which are written with an exponent
        _exponentDoubles = [.. _doubles.Select((value, index) => value * (index % 2 == 0 ? 1e-9 : 1e20))];
        _singles = [.. _doubles.Select(value => (float)value)];
    }

    [Benchmark]
    public long ReadInt32() => Run<int>(0, v => v);

    [Benchmark]
    public long ReadInt64() => Run<long>(1, v => v);

    [Benchmark]
    public long ReadDouble() => Run<double>(2, v => (long)v);

    [Benchmark]
    public long ReadDoubleShort() => Run<double>(3, v => (long)v);

    [Benchmark]
    public long ReadSingle() => Run<float>(7, v => (long)v);

    [Benchmark]
    public long ReadDecimal() => Run<decimal>(4, v => (long)v);

    [Benchmark]
    public long ReadDateTime() => Run<DateTime>(5, v => v.Ticks);

    [Benchmark]
    public long ReadBoolean() => Run<bool>(6, v => v ? 1 : 0);

    /// <summary>Formats the round-trip doubles of the Value column.</summary>
    [Benchmark]
    public long FormatDouble() => Format(_doubles);

    /// <summary>Formats the doubles with one decimal of the Temperature column.</summary>
    [Benchmark]
    public long FormatDoubleShort() => Format(_shortDoubles);

    /// <summary>Formats the round-trip doubles of the Value column, scaled so that they are written with an exponent.</summary>
    [Benchmark]
    public long FormatDoubleExponent() => Format(_exponentDoubles);

    /// <summary>Formats the values of the Value column as floats, in their round-trip format of up to 9 digits.</summary>
    [Benchmark]
    public long FormatSingle() => Format(_singles);

    private long Format<T>(T[] values)
    {
        var converter = CsvMetadataServices.GetConverter<T>(CsvSerializerOptions.Default);
        long sum = 0;
        foreach (var value in values)
        {
            converter.TryFormat(value, _buffer, out var written, _context);
            sum += written;
        }

        return sum;
    }

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
