using FluentAssertions;
using Xunit;

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
}
