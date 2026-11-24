using System.Text;
using Meziantou.Framework.Csv.Serialization;

namespace Meziantou.Framework.Csv.Benchmarks;

#pragma warning disable MA0048 // File name must match type name
#pragma warning disable CA5394 // The data of the benchmarks is not security sensitive

public sealed class BenchmarkRow
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    public decimal Amount { get; set; }

    public DateTime Date { get; set; }

    public bool Active { get; set; }

    public string? Country { get; set; }

    public string? Notes { get; set; }
}

// A record without text: the deserialization is not dominated by the allocation of strings
public sealed class NumericRow
{
    public int Id { get; set; }

    public long Count { get; set; }

    public double Value { get; set; }

    public double Temperature { get; set; }

    public decimal Price { get; set; }

    public DateTime Timestamp { get; set; }

    public bool Flag { get; set; }
}

[CsvSerializable(typeof(BenchmarkRow))]
[CsvSerializable(typeof(NumericRow))]
internal sealed partial class BenchmarkCsvContext : CsvSerializerContext
{
}

internal static class BenchmarkData
{
    private static readonly string[] Countries = ["France", "Germany", "United States", "Japan", "Brazil"];
    private static readonly string[] AccentedCountries = ["France", "Österreich", "日本", "España", "Brasil"];

    public static List<BenchmarkRow> CreateRows(int count)
    {
        var random = new Random(42);
        var rows = new List<BenchmarkRow>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new BenchmarkRow
            {
                Id = i,
                Name = "Name " + i.ToString(CultureInfo.InvariantCulture),
                Email = "user" + i.ToString(CultureInfo.InvariantCulture) + "@example.com",
                Amount = random.Next(0, 1_000_000) / 100m,
                Date = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(random.Next(0, 3_000_000)),
                Active = random.Next(2) == 0,
                Country = Countries[random.Next(Countries.Length)],

                // A tenth of the notes need quotes
                Notes = random.Next(10) == 0 ? "Quoted, with \"quotes\" and a separator" : "Plain note number " + random.Next(1000).ToString(CultureInfo.InvariantCulture),
            });
        }

        return rows;
    }

    // The text values of most rows are not ASCII
    public static List<BenchmarkRow> CreateAccentedRows(int count)
    {
        var random = new Random(42);
        var rows = CreateRows(count);
        foreach (var row in rows)
        {
            row.Name = "Élodie " + row.Id.ToString(CultureInfo.InvariantCulture);
            row.Country = AccentedCountries[random.Next(AccentedCountries.Length)];
            row.Notes = "Note numéro " + random.Next(1000).ToString(CultureInfo.InvariantCulture);
        }

        return rows;
    }

    public static List<NumericRow> CreateNumericRows(int count)
    {
        var random = new Random(42);
        var rows = new List<NumericRow>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new NumericRow
            {
                Id = i,
                Count = random.NextInt64(0, 10_000_000_000),
                Value = random.NextDouble() * 1000,
                Temperature = Math.Round((random.NextDouble() * 60) - 20, 1, MidpointRounding.AwayFromZero),
                Price = random.Next(0, 1_000_000) / 100m,
                Timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(random.Next(0, 3_000_000)),
                Flag = random.Next(2) == 0,
            });
        }

        return rows;
    }

    public static string CreateNumericText(int count) => CsvSerializer.Serialize(CreateNumericRows(count), BenchmarkCsvContext.Default.NumericRow);

    public static string CreateText(int count) =>CsvSerializer.Serialize(CreateRows(count), BenchmarkCsvContext.Default.BenchmarkRow);

    public static byte[] CreateUtf8(int count) => Encoding.UTF8.GetBytes(CreateText(count));
}
