using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotDistinct<T>(ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        for (var duplicateIndex = 1; duplicateIndex < actual.Length; duplicateIndex++)
        {
            if (IndexOf(actual[..duplicateIndex], actual[duplicateIndex], comparer) >= 0)
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanActualValueAssertionError<T>(nameof(NotDistinct), "all distinct items", actual, actualExpression, message)));
    }

    public static void NotDistinct(string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        NotDistinct(actual.AsSpan(), comparer: null, message: message, actualExpression: actualExpression);
    }

    public static void NotDistinct<T>(IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        for (var duplicateIndex = 0; actualSnapshot.TryGetItem(duplicateIndex, out var item); duplicateIndex++)
        {
            if (IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer) >= 0)
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IEnumerable<T>>(nameof(NotDistinct), "all distinct items", actual, actualExpression, message)));
    }

    public static void NotDistinct(System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        for (var duplicateIndex = 0; actualSnapshot.TryGetItem(duplicateIndex, out var item); duplicateIndex++)
        {
            if (IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer) >= 0)
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<System.Collections.IEnumerable>(nameof(NotDistinct), "all distinct items", actual, actualExpression, message)));
    }

    public static async Task NotDistinct<T>(IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        for (var duplicateIndex = 0; await actualSnapshot.TryGetItem(duplicateIndex).ConfigureAwait(false) is (true, var item); duplicateIndex++)
        {
            if (IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer) >= 0)
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(NotDistinct), "all distinct items", AssertionFormatter.FormatExpression(actualExpression), message)));
    }
}
