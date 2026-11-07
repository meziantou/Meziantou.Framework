namespace Meziantou.Framework.Tests;

public sealed class ExceptionExtensionsTests
{
    [Fact]
    public void ToString_WithoutInnerException()
    {
        var exception = CreateException();

        var result = exception.ToString(includeInnerException: false);

        Assert.StartsWith("System.InvalidOperationException: outer" + Environment.NewLine, result, StringComparison.Ordinal);
        Assert.Contains(nameof(CreateException), result, StringComparison.Ordinal);
        Assert.DoesNotContain("inner", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_WithInnerException()
    {
        var exception = CreateException();

        var result = exception.ToString(includeInnerException: true);

        Assert.Equal(exception.ToString(), result);
        Assert.Equal(1, CountOccurrences(result, "System.FormatException: inner"));
    }

    [Fact]
    public void ToString_WithoutStackTrace()
    {
        var exception = new InvalidOperationException("message", new FormatException("inner"));

        Assert.Equal("System.InvalidOperationException: message", exception.ToString(includeInnerException: false));
    }

    private static InvalidOperationException CreateException()
    {
        try
        {
            try
            {
                throw new FormatException("inner");
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("outer", ex);
            }
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }
}
