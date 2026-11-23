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

[CsvSerializable(typeof(BenchmarkRow))]
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

    public static string CreateText(int count) => CsvSerializer.Serialize(CreateRows(count), BenchmarkCsvContext.Default.BenchmarkRow);

    public static byte[] CreateUtf8(int count) => Encoding.UTF8.GetBytes(CreateText(count));
}
