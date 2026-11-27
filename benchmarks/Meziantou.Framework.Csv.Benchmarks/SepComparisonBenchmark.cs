using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Meziantou.Framework.Csv.Serialization;
using nietras.SeparatedValues;

namespace Meziantou.Framework.Csv.Benchmarks;

#pragma warning disable MA0048 // File name must match type name

/// <summary>Reads the same data with this library and with Sep. In each category, Sep is the baseline: a ratio below 1 is faster than Sep.</summary>
/// <remarks>Sep unescapes the quoted values only with <see cref="SepReaderOptions.Unescape"/>: it is set, so both libraries return the same values.</remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SepReadComparisonBenchmark
{
    private string _text = "";
    private byte[] _utf8 = [];

    [Params(CsvDataShape.Unquoted, CsvDataShape.Mixed, CsvDataShape.Quoted)]
    public CsvDataShape Shape { get; set; }

    [Params(100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _text = BenchmarkData.CreateText(Rows, Shape);
        _utf8 = Encoding.UTF8.GetBytes(_text);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Utf16_Spans")]
    public int Sep_Utf16_Spans()
    {
        var length = 0;
        using var reader = Sep.Reader(o => o with { Unescape = true }).FromText(_text);
        foreach (var row in reader)
        {
            for (var i = 0; i < row.ColCount; i++)
            {
                length += row[i].Span.Length;
            }
        }

        return length;
    }

    [Benchmark]
    [BenchmarkCategory("Utf16_Spans")]
    public int Meziantou_Utf16_Spans()
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

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Utf16_Strings")]
    public int Sep_Utf16_Strings()
    {
        var length = 0;
        using var reader = Sep.Reader(o => o with { Unescape = true }).FromText(_text);
        foreach (var row in reader)
        {
            for (var i = 0; i < row.ColCount; i++)
            {
                length += row[i].ToString().Length;
            }
        }

        return length;
    }

    [Benchmark]
    [BenchmarkCategory("Utf16_Strings")]
    public int Meziantou_Utf16_Strings()
    {
        var length = 0;
        using var reader = CsvReader.CreateFromString(_text);
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

    /// <summary>Sep decodes UTF-8 data to UTF-16 before parsing it; this library parses the UTF-8 data.</summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Utf8_Strings")]
    public int Sep_Utf8_Strings()
    {
        var length = 0;
        using var reader = Sep.Reader(o => o with { Unescape = true }).From(_utf8);
        foreach (var row in reader)
        {
            for (var i = 0; i < row.ColCount; i++)
            {
                length += row[i].ToString().Length;
            }
        }

        return length;
    }

    [Benchmark]
    [BenchmarkCategory("Utf8_Strings")]
    public int Meziantou_Utf8_Strings()
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

    [Benchmark]
    [BenchmarkCategory("Utf8_Strings")]
    public int Meziantou_Utf8Stream_Strings()
    {
        var length = 0;
        using var reader = Utf8CsvReader.Create(new MemoryStream(_utf8));
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

    /// <summary>Sep has no serializer: the records are mapped by hand, with the column indexes looked up once from the header.</summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize")]
    public int Sep_Deserialize_Manual()
    {
        var result = new List<BenchmarkRow>();
        using var reader = Sep.Reader(o => o with { Unescape = true }).FromText(_text);
        var header = reader.Header;
        var id = header.IndexOf(nameof(BenchmarkRow.Id));
        var name = header.IndexOf(nameof(BenchmarkRow.Name));
        var email = header.IndexOf(nameof(BenchmarkRow.Email));
        var amount = header.IndexOf(nameof(BenchmarkRow.Amount));
        var date = header.IndexOf(nameof(BenchmarkRow.Date));
        var active = header.IndexOf(nameof(BenchmarkRow.Active));
        var country = header.IndexOf(nameof(BenchmarkRow.Country));
        var notes = header.IndexOf(nameof(BenchmarkRow.Notes));
        foreach (var row in reader)
        {
            result.Add(new BenchmarkRow
            {
                Id = row[id].Parse<int>(),
                Name = row[name].ToString(),
                Email = row[email].ToString(),
                Amount = row[amount].Parse<decimal>(),
                Date = DateTime.Parse(row[date].Span, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Active = row[active].Parse<bool>(),
                Country = row[country].ToString(),
                Notes = row[notes].ToString(),
            });
        }

        return result.Count;
    }

    [Benchmark]
    [BenchmarkCategory("Deserialize")]
    public int Meziantou_Deserialize_Generated() => CsvSerializer.Deserialize(_text, BenchmarkCsvContext.Default.BenchmarkRow).Count;

    [Benchmark]
    [BenchmarkCategory("Deserialize")]
    public int Meziantou_Deserialize_Generated_Utf8() => CsvSerializer.Deserialize(_utf8.AsMemory(), BenchmarkCsvContext.Default.BenchmarkRow).Count;
}

/// <summary>Writes the same records with this library and with Sep. Sep is the baseline: a ratio below 1 is faster than Sep.</summary>
/// <remarks>Sep quotes the values that need it only with <see cref="SepWriterOptions.Escape"/>: it is set, so both libraries write valid CSV data.</remarks>
[MemoryDiagnoser]
public class SepWriteComparisonBenchmark
{
    private List<BenchmarkRow> _rows = [];

    [Params(CsvDataShape.Unquoted, CsvDataShape.Mixed)]
    public CsvDataShape Shape { get; set; }

    [Params(100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rows = BenchmarkData.CreateRows(Rows, Shape);
    }

    /// <summary>Sep has no serializer: the values are written by hand, with the same formats as the serializer.</summary>
    [Benchmark(Baseline = true)]
    public int Sep_Serialize_Manual()
    {
        using var writer = Sep.Writer(o => o with { Escape = true }).ToText();
        foreach (var value in _rows)
        {
            using var row = writer.NewRow();
            row[nameof(BenchmarkRow.Id)].Format(value.Id);
            row[nameof(BenchmarkRow.Name)].Set(value.Name);
            row[nameof(BenchmarkRow.Email)].Set(value.Email);
            row[nameof(BenchmarkRow.Amount)].Format(value.Amount);
            row[nameof(BenchmarkRow.Date)].Set($"{value.Date:O}");
            row[nameof(BenchmarkRow.Active)].Set(value.Active ? "true" : "false");
            row[nameof(BenchmarkRow.Country)].Set(value.Country);
            row[nameof(BenchmarkRow.Notes)].Set(value.Notes);
        }

        return writer.ToString().Length;
    }

    [Benchmark]
    public int Meziantou_Serialize_Generated() => CsvSerializer.Serialize(_rows, BenchmarkCsvContext.Default.BenchmarkRow).Length;

    [Benchmark]
    public long Meziantou_Serialize_Generated_Utf8()
    {
        using var stream = new MemoryStream();
        CsvSerializer.Serialize(stream, _rows, BenchmarkCsvContext.Default.BenchmarkRow);
        return stream.Length;
    }
}
