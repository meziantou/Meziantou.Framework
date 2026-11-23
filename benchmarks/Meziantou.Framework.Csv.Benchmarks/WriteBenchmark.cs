using System.Buffers;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Csv.Benchmarks;

/// <summary>Writes rows of string values, and rows of values formatted in the buffer of the writer.</summary>
[MemoryDiagnoser]
public class WriteBenchmark
{
    private string[][] _rows = [];
    private List<BenchmarkRow> _records = [];

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _records = BenchmarkData.CreateRows(Rows);
        _rows = [.. _records.Select(row => new[] { row.Id.ToString(CultureInfo.InvariantCulture), row.Name!, row.Email!, row.Country!, row.Notes! })];
    }

    [Benchmark(Baseline = true)]
    public int Utf16_Strings()
    {
        var builder = new StringBuilder();
        using (var writer = CsvWriter.Create(builder))
        {
            foreach (var row in _rows)
            {
                writer.WriteRow(row);
            }
        }

        return builder.Length;
    }

    /// <summary>Writes rows of string values that are all quoted.</summary>
    [Benchmark]
    public int Utf16_Strings_AlwaysQuote()
    {
        var builder = new StringBuilder();
        using (var writer = CsvWriter.Create(builder, new CsvWriterOptions { QuoteMode = CsvQuoteMode.Always }))
        {
            foreach (var row in _rows)
            {
                writer.WriteRow(row);
            }
        }

        return builder.Length;
    }

    [Benchmark]
    public int Utf8_Strings()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = Utf8CsvWriter.Create(buffer))
        {
            foreach (var row in _rows)
            {
                writer.WriteRow(row);
            }
        }

        return buffer.WrittenCount;
    }

    /// <summary>Writes rows of string values as UTF-8, all quoted.</summary>
    [Benchmark]
    public int Utf8_Strings_AlwaysQuote()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = Utf8CsvWriter.Create(buffer, new CsvWriterOptions { QuoteMode = CsvQuoteMode.Always }))
        {
            foreach (var row in _rows)
            {
                writer.WriteRow(row);
            }
        }

        return buffer.WrittenCount;
    }

    [Benchmark]
    public int Utf16_Formatted()
    {
        var builder = new StringBuilder();
        using (var writer = CsvWriter.Create(builder))
        {
            foreach (var record in _records)
            {
                writer.WriteField(record.Id);
                writer.WriteField(record.Amount);
                writer.WriteField(record.Date, "O");
                writer.EndRow();
            }
        }

        return builder.Length;
    }

    [Benchmark]
    public int Utf8_Formatted()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = Utf8CsvWriter.Create(buffer))
        {
            foreach (var record in _records)
            {
                writer.WriteField(record.Id);
                writer.WriteField(record.Amount);
                writer.WriteField(record.Date, "O");
                writer.EndRow();
            }
        }

        return buffer.WrittenCount;
    }
}
