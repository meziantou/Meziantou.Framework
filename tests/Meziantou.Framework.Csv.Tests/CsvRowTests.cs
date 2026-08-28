namespace Meziantou.Framework.Csv.Tests;

public class CsvRowTests
{
    [Fact]
    public void GetValueExtensionIsAvailable()
    {
        // Arrange
        var columns = new List<CsvColumn> { new("test", 0) };
        var values = new List<string> { "42" };
        var row = new CsvRow(columns, values);

        // Act
        var actual = row.GetValueOrDefault("test", 0);
        Assert.Equal(42, actual);
    }

    [Fact]
    public async Task RaggedRow_ValueOfMissingColumnIsNull()
    {
        var row = await ReadSingleRowWithHeaderAsync("A,B,C\n1,2");

        Assert.Equal("1", row["A"]);
        Assert.Equal("2", row["B"]);
        Assert.Null(row["C"]);
        Assert.Null(row["unknown"]);
    }

    [Fact]
    public async Task RaggedRow_TryGetValueDoesNotThrow()
    {
        var row = (IReadOnlyDictionary<string, string?>)await ReadSingleRowWithHeaderAsync("A,B,C\n1,2");

        Assert.True(row.TryGetValue("C", out var value));
        Assert.Null(value);

        Assert.True(row.TryGetValue("A", out var a));
        Assert.Equal("1", a);

        Assert.False(row.TryGetValue("unknown", out var unknown));
        Assert.Null(unknown);
    }

    [Fact]
    public async Task RaggedRow_ContainsKeyAgreesWithTryGetValue()
    {
        var row = (IReadOnlyDictionary<string, string?>)await ReadSingleRowWithHeaderAsync("A,B,C\n1,2");

        foreach (var key in new[] { "A", "B", "C", "unknown" })
        {
            Assert.Equal(row.ContainsKey(key), row.TryGetValue(key, out _));
        }
    }

    [Fact]
    public async Task RaggedRow_CountAgreesWithKeysAndEnumeration()
    {
        var row = (IReadOnlyDictionary<string, string?>)await ReadSingleRowWithHeaderAsync("A,B,C\n1,2");

        Assert.Equal(3, row.Count);
        Assert.Equal(["A", "B", "C"], row.Keys);
    }

    [Fact]
    public async Task RaggedRow_EnumerationDoesNotThrow()
    {
        var row = (IReadOnlyDictionary<string, string?>)await ReadSingleRowWithHeaderAsync("A,B,C\n1,2");

        Assert.Equal(
            [
                new KeyValuePair<string, string?>("A", "1"),
                new KeyValuePair<string, string?>("B", "2"),
                new KeyValuePair<string, string?>("C", null),
            ],
            row.ToList());
    }

    [Fact]
    public async Task CompleteRow_DictionaryViewIsConsistent()
    {
        var row = (IReadOnlyDictionary<string, string?>)await ReadSingleRowWithHeaderAsync("A,B\n1,2");

        Assert.Equal(2, row.Count);
        Assert.Equal(["A", "B"], row.Keys);
        Assert.Equal(["1", "2"], row.Values);
        Assert.True(row.TryGetValue("B", out var b));
        Assert.Equal("2", b);
    }

    [Fact]
    public void RowWithoutHeader_ValueByColumnOutOfRangeIsNull()
    {
        var row = new CsvRow(columns: null, ["1", "2"]);

        Assert.Null(row[new CsvColumn("C", 2)]);
        Assert.Equal("1", row[new CsvColumn("A", 0)]);
    }

    [Fact]
    public void IndexerByPositionStillThrowsOutOfRange()
    {
        var row = new CsvRow(columns: null, ["1", "2"]);

        Assert.Throws<ArgumentOutOfRangeException>(() => row[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => row[-1]);
    }

    private static async Task<CsvRow> ReadSingleRowWithHeaderAsync(string csv)
    {
        using var sr = new StringReader(csv);
        var reader = new CsvReader(sr) { HasHeaderRow = true };
        var row = await reader.ReadRowAsync();
        Assert.NotNull(row);
        return row;
    }
}
