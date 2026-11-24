using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Csv.Serialization;

namespace Meziantou.Framework.Csv.Benchmarks;

/// <summary>Reads every value of every row, as spans and as strings.</summary>
[MemoryDiagnoser]
public class ReadBenchmark
{
    private string _text = "";
    private byte[] _utf8 = [];
    private byte[] _accentedUtf8 = [];

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _text = BenchmarkData.CreateText(Rows);
        _utf8 = BenchmarkData.CreateUtf8(Rows);
        _accentedUtf8 = Encoding.UTF8.GetBytes(CsvSerializer.Serialize(BenchmarkData.CreateAccentedRows(Rows), BenchmarkCsvContext.Default.BenchmarkRow));
    }

    [Benchmark(Baseline = true)]
    public int Utf16_Spans()
    {
        var length = 0;
        using var reader = CsvReader.CreateFromString(_text);
        while (reader.Read())
        {
            var record = reader.Current;
            for (var i = 0; i < record.ColumnCount; i++)
            {
                length += record[i].Span.Length;
            }
        }

        return length;
    }

    /// <summary>Reads each value by the name of its column, as most code that reads a file with a header does.</summary>
    [Benchmark]
    public int Utf16_Spans_ByName()
    {
        var length = 0;
        using var reader = CsvReader.CreateFromString(_text);
        while (reader.Read())
        {
            var record = reader.Current;
            length += record["Id"].Span.Length + record["Name"].Span.Length + record["Email"].Span.Length + record["Amount"].Span.Length +
                record["Date"].Span.Length + record["Active"].Span.Length + record["Country"].Span.Length + record["Notes"].Span.Length;
        }

        return length;
    }

    [Benchmark]
    public int Utf16TextReader_Spans()
    {
        var length = 0;
        using var reader = CsvReader.Create(new StringReader(_text));
        while (reader.Read())
        {
            var record = reader.Current;
            for (var i = 0; i < record.ColumnCount; i++)
            {
                length += record[i].Span.Length;
            }
        }

        return length;
    }

    [Benchmark]
    public int Utf8_Spans()
    {
        var length = 0;
        using var reader = Utf8CsvReader.Create(_utf8);
        while (reader.Read())
        {
            var record = reader.Current;
            for (var i = 0; i < record.ColumnCount; i++)
            {
                length += record[i].Utf8Span.Length;
            }
        }

        return length;
    }

    [Benchmark]
    public int Utf8Stream_Spans()
    {
        var length = 0;
        using var reader = Utf8CsvReader.Create(new MemoryStream(_utf8));
        while (reader.Read())
        {
            var record = reader.Current;
            for (var i = 0; i < record.ColumnCount; i++)
            {
                length += record[i].Utf8Span.Length;
            }
        }

        return length;
    }

    [Benchmark]
    public int Utf16_Strings()
    {
        var length = 0;
        using var reader = CsvReader.Create(new StringReader(_text));
        while (reader.Read())
        {
            var record = reader.Current;
            for (var i = 0; i < record.ColumnCount; i++)
            {
                length += record[i].ToString().Length;
            }
        }

        return length;
    }

    [Benchmark]
    public int Utf8_Strings()
    {
        var length = 0;
        using var reader = Utf8CsvReader.Create(_utf8);
        while (reader.Read())
        {
            var record = reader.Current;
            for (var i = 0; i < record.ColumnCount; i++)
            {
                length += record[i].ToString().Length;
            }
        }

        return length;
    }

    /// <summary>Reads the UTF-8 values as strings when most text values are not ASCII.</summary>
    [Benchmark]
    public int Utf8_Strings_Accented()
    {
        var length = 0;
        using var reader = Utf8CsvReader.Create(_accentedUtf8);
        while (reader.Read())
        {
            var record = reader.Current;
            for (var i = 0; i < record.ColumnCount; i++)
            {
                length += record[i].ToString().Length;
            }
        }

        return length;
    }
}
