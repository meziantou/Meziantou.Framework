#pragma warning disable MA0048 // File name must match type name
using System.Text;
using Meziantou.Framework.Csv.Serialization;

namespace Meziantou.Framework.Csv.AotTests;

public sealed class CsvAotTests
{
    [Fact]
    public void ReadAndWrite()
    {
        var output = new StringBuilder();
        using (var writer = CsvWriter.Create(output, new CsvWriterOptions { NewLine = "\n" }))
        {
            writer.WriteRow("Name", "Value");
            writer.WriteField("a,b");
            writer.WriteField(42);
            writer.EndRow();
        }

        using var reader = CsvReader.CreateFromString(output.ToString());
        Assert.True(reader.Read());
        Assert.Equal("a,b", reader.Current["Name"].ToString());
        Assert.Equal(42, reader.Current["Value"].Parse<int>());

        using var utf8Reader = Utf8CsvReader.Create(Encoding.UTF8.GetBytes(output.ToString()));
        Assert.True(utf8Reader.Read());
        Assert.Equal(42, utf8Reader.Current["Value"].Parse<int>());
    }

    [Fact]
    public void RoundTrip()
    {
        AotItem[] items = [new() { Name = "a, b", Count = 1, Kind = AotKind.Second, Date = new DateOnly(2026, 11, 21), Ratio = 1.5 }, new() { Name = "c" }];
        var csv = CsvSerializer.Serialize(items, AotCsvContext.Default.AotItem);
        Assert.Equal("Name,Count,Kind,Date,Ratio\r\n\"a, b\",1,Second,2026-11-21,1.5\r\nc,0,First,,\r\n", csv);

        var result = CsvSerializer.Deserialize(csv, AotCsvContext.Default.AotItem);
        Assert.Equal(2, result.Count);
        Assert.Equal("a, b", result[0].Name);
        Assert.Equal(AotKind.Second, result[0].Kind);
        Assert.Null(result[1].Date);

        using var stream = new MemoryStream();
        CsvSerializer.Serialize(stream, items, AotCsvContext.Default.AotItem);
        stream.Position = 0;
        Assert.Equal(1.5, CsvSerializer.Deserialize(stream, AotCsvContext.Default.AotItem)[0].Ratio);
    }

    // Constructors, init accessors and non-public members are reached through [UnsafeAccessor] stubs, which Native AOT supports
    [Fact]
    public void RecordsAndNonPublicMembers()
    {
        var records = CsvSerializer.Deserialize("Id,Label\n1,one", AotCsvContext.Default.AotRecord);
        Assert.Equal(new AotRecord(1, "one"), records[0]);

        var restricted = CsvSerializer.Deserialize("Init,Required,Secret\n1,2,3", AotCsvContext.Default.AotRestricted)[0];
        Assert.Equal(1, restricted.Init);
        Assert.Equal(2, restricted.Required);
        Assert.Equal(3, restricted.GetSecret());
        Assert.Equal("Init,Required,Secret\r\n1,2,3\r\n", CsvSerializer.Serialize([restricted], AotCsvContext.Default.AotRestricted));
    }

    // The AOT compiler ignores #pragma: only these attributes suppress its warnings
    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The test checks that the reflection-based API fails")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The test checks that the reflection-based API fails")]
    public void ReflectionIsDisabled()
    {
        Assert.False(CsvSerializer.IsReflectionEnabledByDefault);
        Assert.Throws<InvalidOperationException>(() => CsvSerializer.Deserialize<AotItem>("Name\na"));
    }
}

public enum AotKind
{
    First,
    Second,
}

public sealed class AotItem
{
    public string? Name { get; set; }

    public int Count { get; set; }

    public AotKind Kind { get; set; }

    public DateOnly? Date { get; set; }

    public double? Ratio { get; set; }
}

public sealed record AotRecord(int Id, string Label);

public sealed class AotRestricted
{
    public int Init { get; init; }

    public required int Required { get; set; }

    [CsvInclude]
    private int Secret { get; set; }

    public int GetSecret() => Secret;
}

[CsvSerializable(typeof(AotItem))]
[CsvSerializable(typeof(AotRecord))]
[CsvSerializable(typeof(AotRestricted))]
internal sealed partial class AotCsvContext : CsvSerializerContext
{
}
