using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotEmpty<T>(ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (!actual.IsEmpty)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanActualValueAssertionError<T>(nameof(NotEmpty), "empty", actual, actualExpression, message)));
    }

    public static void NotEmpty(string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<string>(nameof(NotEmpty), "empty", actual, actualExpression, message)));
    }

    public static void NotEmpty<T>(IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (actualEnumerator.MoveNext())
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IEnumerable<T>>(nameof(NotEmpty), "empty", actualSnapshot.Items, actualExpression, message)));
    }

    public static void NotEmpty(System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (actualEnumerator.MoveNext())
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IReadOnlyList<object?>>(nameof(NotEmpty), "empty", actualSnapshot.Items, actualExpression, message)));
    }

    public static async Task NotEmpty<T>(IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();

        if (await actualEnumerator.MoveNextAsync().ConfigureAwait(false))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IReadOnlyList<T>>(nameof(NotEmpty), "empty", actualSnapshot.Items, actualExpression, message)));
    }
}
