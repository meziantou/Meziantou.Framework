using AssertionException = Meziantou.Framework.Assertions.AssertionException;

namespace Meziantou.Framework.Assertions.Tests;

internal static class AssertionTestHelpers
{
    public static void Validate(Action action, string expectedMessage)
    {
        var exception = global::Xunit.Assert.Throws<AssertionException>(action);
        global::Xunit.Assert.Equal(expectedMessage, exception.Message);
    }

    public static async Task ValidateAsync(Func<Task> action, string expectedMessage)
    {
        var exception = await global::Xunit.Assert.ThrowsAsync<AssertionException>(action);
        global::Xunit.Assert.Equal(expectedMessage, exception.Message);
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
