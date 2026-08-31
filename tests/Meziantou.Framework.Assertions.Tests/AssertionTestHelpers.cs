using System.Runtime.CompilerServices;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

internal static class AssertionTestHelpers
{
    [ModuleInitializer]
    public static void InitializeFormatterOptions()
    {
        AssertionsAssert.FormatterOptions = new()
        {
            MaxFormattedItems = 10,
            PrefixItemCount = 3,
            HighlightedContextItemCount = 2,
        };
    }

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

    /// <summary>
    /// A sequence that can only be enumerated once, so an assertion that walks it a second time to build its failure
    /// message fails loudly instead of silently reporting different data.
    /// </summary>
    public static IEnumerable<T> SingleUse<T>(params T[] items) => new SingleUseEnumerable<T>(items);

    private sealed class SingleUseEnumerable<T>(T[] items) : IEnumerable<T>
    {
        private int _enumerationCount;

        public IEnumerator<T> GetEnumerator()
        {
            _enumerationCount++;
            if (_enumerationCount > 1)
                throw new InvalidOperationException("The sequence was enumerated more than once.");

            return ((IEnumerable<T>)items).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
