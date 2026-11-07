namespace Meziantou.Framework.Tests;

public class StringBuilderExtensionsTests
{
    [Fact]
    public void AppendInvariant_FormattableString()
    {
        CultureInfoUtilities.UseCulture("sv-SE", () =>
        {
            var actual = new StringBuilder().AppendInvariant($"test{-42}").ToString();
            Assert.Equal("test-42", actual);
        });

        CultureInfoUtilities.UseCulture("en-US", () =>
        {
            var actual = new StringBuilder().AppendInvariant($"test{-42}").ToString();
            Assert.Equal("test-42", actual);
        });
    }

    [Theory]
    [InlineData("", 'a', false)]
    [InlineData("abc", 'c', true)]
    [InlineData("abc", 'd', false)]
    public void EndsWith_Test(string str, char c, bool expected)
    {
        var actual = new StringBuilder(str).EndsWith(c);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("", 'a', false)]
    [InlineData("abc", 'a', true)]
    [InlineData("abc", 'c', false)]
    public void StartsWith_Test(string str, char c, bool expected)
    {
        var actual = new StringBuilder(str).StartsWith(c);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("", 'a', "")]
    [InlineData("abc", 'a', "bc")]
    [InlineData("aaabc", 'a', "bc")]
    [InlineData("a", 'a', "")]
    [InlineData("aaa", 'a', "")]
    [InlineData("abc", 'b', "abc")]
    public void TrimStart(string str, char c, string expected)
    {
        var actual = new StringBuilder(str);
        actual.TrimStart(c);
        Assert.Equal(expected, actual.ToString());
    }

    [Theory]
    [InlineData("", 'a', "")]
    [InlineData("abc", 'c', "ab")]
    [InlineData("abccc", 'c', "ab")]
    [InlineData("c", 'c', "")]
    [InlineData("ccc", 'c', "")]
    [InlineData("abc", 'b', "abc")]
    public void TrimEnd(string str, char c, string expected)
    {
        var actual = new StringBuilder(str);
        actual.TrimEnd(c);
        Assert.Equal(expected, actual.ToString());
    }

    [Theory]
    [InlineData("", 'a', "")]
    [InlineData("abc", 'c', "ab")]
    [InlineData("cccabccc", 'c', "ab")]
    [InlineData("cccacbccc", 'c', "acb")]
    [InlineData("a", 'a', "")]
    [InlineData("aaa", 'a', "")]
    [InlineData("abc", 'b', "abc")]
    public void Trim(string str, char c, string expected)
    {
        var actual = new StringBuilder(str);
        actual.Trim(c);
        Assert.Equal(expected, actual.ToString());
    }

    [Fact]
    public void Documentation_StartsWithAndTrim()
    {
        #region StringBuilderExtensions_StartsWithAndTrim
        var sb = new StringBuilder();
        foreach (var item in new[] { "apple", "banana", "cherry" })
        {
            sb.Append(item).Append(',');
        }

        // Check and remove the trailing separator without converting the builder to a string
        Assert.True(sb.EndsWith(','));
        sb.TrimEnd(',');
        Assert.Equal("apple,banana,cherry", sb.ToString());
        Assert.True(sb.StartsWith("apple"));
        #endregion
    }

    [Fact]
    public void Documentation_AppendInvariant()
    {
        #region StringBuilderExtensions_AppendInvariant
        var sb = new StringBuilder();

        // The values are formatted with the invariant culture, whatever the current culture
        sb.AppendInvariant(1234.5).Append(' ');
        sb.AppendInvariant($"{0.25:P0} of {1000:N0}").Append(' ');
        sb.AppendFormatInvariant("{0:F2}", 3.14159);

        Assert.Equal("1234.5 25 % of 1,000 3.14", sb.ToString());
        #endregion
    }
}
