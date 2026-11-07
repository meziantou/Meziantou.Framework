using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    // Arrays would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void NotEmpty<T>([NotNull] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotEmpty), actualExpression, message, "Not expected", "empty");
        }

        NotEmpty(new ReadOnlySpan<T>(actual), message, actualExpression);
    }

    public static void NotEmpty<T>(ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (!actual.IsEmpty)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanActualValueAssertionError<T>(nameof(NotEmpty), "empty", actual, actualExpression, message)));
    }

    public static void NotEmpty([NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotEmpty), actualExpression, message, "Not expected", "empty");
        }

        if (actual.Length != 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<string>(nameof(NotEmpty), "empty", actual, actualExpression, message)));
    }

    public static void NotEmpty<T>([NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotEmpty), actualExpression, message, "Not expected", "empty");
        }

        if (TryGetKnownCount(actual, out var knownCount) && knownCount > 0)
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (actualSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IEnumerable<T>>(nameof(NotEmpty), "empty", actualSnapshot.Items, actualExpression, message)));
    }

    public static void NotEmpty([NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotEmpty), actualExpression, message, "Not expected", "empty");
        }

        if (actual is System.Collections.ICollection { Count: > 0 })
            return;

        using var actualSnapshot = CollectionSnapshot.Create(actual);

        if (actualSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IReadOnlyList<object?>>(nameof(NotEmpty), "empty", actualSnapshot.Items, actualExpression, message)));
    }

    public static async Task NotEmpty<T>([NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotEmpty), actualExpression, message, "Not expected", "empty");
        }

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (await actualSnapshot.TryGetItem(0).ConfigureAwait(false) is (true, _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IReadOnlyList<T>>(nameof(NotEmpty), "empty", actualSnapshot.Items, actualExpression, message)));
    }
}
