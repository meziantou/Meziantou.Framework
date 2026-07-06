namespace Meziantou.Framework.Tests;

public class DateTimeUtilitiesTests
{
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
}
