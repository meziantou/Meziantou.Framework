using AssertionException = Meziantou.Framework.Assertions.AssertionException;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

internal static class AssertionTestHelpers
{
    public static void Validate(Action action, string expectedMessage)
    {
        var exception = AssertionsAssert.Throws<AssertionException>(action);
        AssertionsAssert.Equal(expectedMessage, exception.Message);
    }

    public static async Task ValidateAsync(Func<Task> action, string expectedMessage)
    {
        var exception = await AssertionsAssert.Throws<AssertionException>(action);
        AssertionsAssert.Equal(expectedMessage, exception.Message);
    }

    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
