using Meziantou.Framework;

namespace Meziantou.Framework.Tests;

public sealed class AsyncEnumerableExtensionsTests
{
    [Fact]
    public async Task WhereNotNullTests()
    {
        Assert.Equal(["a", "b"], await CreateEnumerable("a", null, "b").WhereNotNull().ToListAsync());
    }

    private static async IAsyncEnumerable<T> CreateEnumerable<T>(params T[] items)
    {
        await Task.Yield();
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
