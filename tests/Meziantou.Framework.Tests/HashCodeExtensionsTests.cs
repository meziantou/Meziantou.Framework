namespace Meziantou.Framework.Tests;

public sealed class HashCodeExtensionsTests
{
    [Fact]
    public void AddValues_Array_ContributesToTheHashCode()
    {
        var hashCode = new HashCode();
        hashCode.AddValues([1, 2, 3]);

        Assert.NotEqual(new HashCode().ToHashCode(), hashCode.ToHashCode());
    }

    [Fact]
    public void AddValues_Span_ContributesToTheHashCode()
    {
        var hashCode = new HashCode();
        hashCode.AddValues(new ReadOnlySpan<int>([1, 2, 3]));

        Assert.NotEqual(new HashCode().ToHashCode(), hashCode.ToHashCode());
    }

    [Fact]
    public void AddValues_Enumerable_ContributesToTheHashCode()
    {
        var hashCode = new HashCode();
        hashCode.AddValues(Enumerable.Range(1, 3));

        Assert.NotEqual(new HashCode().ToHashCode(), hashCode.ToHashCode());
    }

    [Fact]
    public void AddValues_MatchesAddingTheValuesOneByOne()
    {
        var actual = new HashCode();
        actual.AddValues([1, 2, 3]);

        var expected = new HashCode();
        expected.Add(1);
        expected.Add(2);
        expected.Add(3);

        Assert.Equal(expected.ToHashCode(), actual.ToHashCode());
    }

    [Fact]
    public void AddValues_DifferentValuesProduceDifferentHashCodes()
    {
        var first = new HashCode();
        first.AddValues([1, 2, 3]);

        var second = new HashCode();
        second.AddValues([4, 5, 6]);

        Assert.NotEqual(first.ToHashCode(), second.ToHashCode());
    }

    [Fact]
    public void AddValues_UsesTheEqualityComparer()
    {
        var withComparer = new HashCode();
        withComparer.AddValues(["A"], StringComparer.OrdinalIgnoreCase);

        var expected = new HashCode();
        expected.Add("a", StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expected.ToHashCode(), withComparer.ToHashCode());
    }
}
