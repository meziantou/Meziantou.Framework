namespace Meziantou.Framework.Tests;

public static class DictionaryExtensionsTests
{
    [Fact]
    public static void ReadmeExample_DictionaryExtensions()
    {
        #region TypeConverter_DictionaryExtensions
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["age"] = "25",
            ["price"] = 19.99,
            ["active"] = true,
        };

        // Get and convert values with type safety
        var age = dictionary.GetValueOrDefault("age", 0);
        Assert.Equal(25, age); // as int

        var price = dictionary.GetValueOrDefault("price", 0.0);
        Assert.Equal(19.99, price); // as double

        // Use default value when key doesn't exist
        var missing = dictionary.GetValueOrDefault("missing", "default");
        Assert.Equal("default", missing);

        // TryGetValueOrDefault for better control
        Assert.True(dictionary.TryGetValueOrDefault("age", out int ageValue));
        Assert.Equal(25, ageValue);
        #endregion
    }

    [Fact]
    public static void GetValue_KeyExists()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "test", 42 },
        };

        // Act
        var actual = dictionary.GetValueOrDefault("test", "");
        Assert.Equal("42", actual);
    }

    [Fact]
    public static void GetValue_KeyNotExists()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "test", 42 },
        };

        // Act
        var actual = dictionary.GetValueOrDefault("unknown", "");
        Assert.Empty(actual);
    }

    [Fact]
    public static void GetValue_KeyNotConvertible()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            { "test", "aaa" },
        };

        // Act
        var actual = dictionary.GetValueOrDefault("test", 0);
        Assert.Equal(0, actual);
    }
}
