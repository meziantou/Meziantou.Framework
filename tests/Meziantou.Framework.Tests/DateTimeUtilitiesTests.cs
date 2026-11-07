namespace Meziantou.Framework.Tests;

public class DateTimeUtilitiesTests
{
    [Fact]
    public void Documentation_StartAndEnd()
    {
        #region DateTimeExtensions_StartAndEnd
        var date = new DateTime(2026, 10, 29, 14, 30, 15, 250); // A Thursday

        // StartOfWeek keeps the time of day
        Assert.Equal(new DateTime(2026, 10, 26, 14, 30, 15, 250), date.StartOfWeek(DayOfWeek.Monday));

        // StartOfMonth and StartOfYear return midnight, unless keepTime is true
        Assert.Equal(new DateTime(2026, 10, 1), date.StartOfMonth());
        Assert.Equal(new DateTime(2026, 10, 1, 14, 30, 15, 250), date.StartOfMonth(keepTime: true));
        Assert.Equal(new DateTime(2026, 1, 1), date.StartOfYear());

        // EndOfMonth returns midnight of the last day of the month
        Assert.Equal(new DateTime(2026, 10, 31), date.EndOfMonth());

        // TruncateMilliseconds removes everything below the second
        Assert.Equal(new DateTime(2026, 10, 29, 14, 30, 15), date.TruncateMilliseconds());
        #endregion
    }

    [Fact]
    public void StartOfWeek_01()
    {
        // Arrange
        var dt = new DateTime(2015, 05, 17);

        // Act
        var actual = DateTimeExtensions.StartOfWeek(dt, DayOfWeek.Sunday);

        // Assert
        var expected = new DateTime(2015, 05, 17);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StartOfWeek_02()
    {
        // Arrange
        var dt = new DateTime(2015, 05, 17);

        // Act
        var actual = DateTimeExtensions.StartOfWeek(dt, DayOfWeek.Monday);

        // Assert
        var expected = new DateTime(2015, 05, 11);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TruncateMilliseconds()
    {
        // Arrange
        var dt = new DateTime(2018, 2, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        // Act
        var actual = DateTimeExtensions.TruncateMilliseconds(dt);

        // Assert
        var expected = new DateTime(2018, 2, 3, 4, 5, 6, 0, DateTimeKind.Utc);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void StartOfMonth_PreservesKind(DateTimeKind kind)
    {
        var value = new DateTime(2024, 5, 17, 13, 45, 30, kind);

        Assert.Equal(kind, value.StartOfMonth().Kind);
        Assert.Equal(kind, value.StartOfMonth(keepTime: true).Kind);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void EndOfMonth_PreservesKind(DateTimeKind kind)
    {
        var value = new DateTime(2024, 5, 17, 13, 45, 30, kind);

        Assert.Equal(kind, value.EndOfMonth().Kind);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void StartOfYear_PreservesKind(DateTimeKind kind)
    {
        var value = new DateTime(2024, 5, 17, 13, 45, 30, kind);

        Assert.Equal(kind, value.StartOfYear().Kind);
        Assert.Equal(kind, value.StartOfYear(keepTime: true).Kind);
    }

    [Fact]
    public void StartOfMonth_StillReturnsTheFirstDayAtMidnight()
    {
        var value = new DateTime(2024, 5, 17, 13, 45, 30, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc), value.StartOfMonth());
    }

    [Fact]
    public void EndOfMonth_StillReturnsTheLastDayAtMidnight()
    {
        var value = new DateTime(2024, 2, 3, 13, 45, 30, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc), value.EndOfMonth());
    }
}
