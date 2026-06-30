using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotEmpty<T>(ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (!actual.IsEmpty)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanActualValueAssertionError<T>(nameof(NotEmpty), "empty", actual, actualExpression, message: null)));
    }

    public static void NotEmpty(string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<string>(nameof(NotEmpty), "empty", actual, actualExpression, message: null)));
    }

    public static void NotEmpty<T>(IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (actualSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IEnumerable<T>>(nameof(NotEmpty), "empty", actualSnapshot.Items, actualExpression, message: null)));
    }

    public static void NotEmpty(System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);

        if (actualSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IReadOnlyList<object?>>(nameof(NotEmpty), "empty", actualSnapshot.Items, actualExpression, message: null)));
    }

    public static async Task NotEmpty<T>(IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (await actualSnapshot.TryGetItem(0).ConfigureAwait(false) is (true, _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IReadOnlyList<T>>(nameof(NotEmpty), "empty", actualSnapshot.Items, actualExpression, message: null)));
    }
}
