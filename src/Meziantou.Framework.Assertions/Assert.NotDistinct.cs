using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    // Arrays would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void NotDistinct<T>([NotNull] T[]? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotDistinct), actualExpression, message, "Not expected", "all distinct items");
        }

        NotDistinct(new ReadOnlySpan<T>(actual), comparer, message, actualExpression);
    }

    public static void NotDistinct<T>(ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        if (actual.Length <= LinearDuplicateSearchThreshold)
        {
            for (var duplicateIndex = 1; duplicateIndex < actual.Length; duplicateIndex++)
            {
                if (IndexOf(actual[..duplicateIndex], actual[duplicateIndex], comparer) >= 0)
                    return;
            }
        }
        else
        {
            var firstIndexes = new FirstIndexLookup<T>(comparer, actual.Length);
            for (var duplicateIndex = 0; duplicateIndex < actual.Length; duplicateIndex++)
            {
                if (firstIndexes.Add(actual[duplicateIndex], duplicateIndex) >= 0)
                    return;
            }
        }

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanActualValueAssertionError<T>(nameof(NotDistinct), "all distinct items", actual, actualExpression, message)));
    }

    public static void NotDistinct([NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotDistinct), actualExpression, message, "Not expected", "all distinct items");
        }

        NotDistinct(actual.AsSpan(), comparer: null, message: message, actualExpression: actualExpression);
    }

    public static void NotDistinct<T>([NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotDistinct), actualExpression, message, "Not expected", "all distinct items");
        }

        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        FirstIndexLookup<T>? firstIndexes = null;
        for (var duplicateIndex = 0; actualSnapshot.TryGetItem(duplicateIndex, out var item); duplicateIndex++)
        {
            if (duplicateIndex < LinearDuplicateSearchThreshold)
            {
                if (IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer) >= 0)
                    return;
            }
            else
            {
                firstIndexes ??= FirstIndexLookup<T>.Create(actualSnapshot.Items, duplicateIndex, comparer, TryGetKnownCount(actual, out var knownCount) ? knownCount : duplicateIndex);
                if (firstIndexes.Add(item, duplicateIndex) >= 0)
                    return;
            }
        }

        actualSnapshot.EnsureComplete();
        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IReadOnlyList<T>>(nameof(NotDistinct), "all distinct items", actualSnapshot.Items, actualExpression, message)));
    }

    public static void NotDistinct([NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotDistinct), actualExpression, message, "Not expected", "all distinct items");
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        FirstIndexLookup<object?>? firstIndexes = null;
        for (var duplicateIndex = 0; actualSnapshot.TryGetItem(duplicateIndex, out var item); duplicateIndex++)
        {
            if (duplicateIndex < LinearDuplicateSearchThreshold)
            {
                if (IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer) >= 0)
                    return;
            }
            else
            {
                // ObjectEqualityComparer hashes with the comparer, or with object.GetHashCode, which agrees with the
                // object.Equals comparison used by the linear scan
                firstIndexes ??= FirstIndexLookup<object?>.Create(actualSnapshot.Items, duplicateIndex, new ObjectEqualityComparer(comparer), duplicateIndex);
                if (firstIndexes.Add(item, duplicateIndex) >= 0)
                    return;
            }
        }

        actualSnapshot.EnsureComplete();
        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IReadOnlyList<object?>>(nameof(NotDistinct), "all distinct items", actualSnapshot.Items, actualExpression, message)));
    }

    public static async Task NotDistinct<T>([NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(NotDistinct), actualExpression, message, "Not expected", "all distinct items");
        }

        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        FirstIndexLookup<T>? firstIndexes = null;
        for (var duplicateIndex = 0; await actualSnapshot.TryGetItem(duplicateIndex).ConfigureAwait(false) is (true, var item); duplicateIndex++)
        {
            if (duplicateIndex < LinearDuplicateSearchThreshold)
            {
                if (IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer) >= 0)
                    return;
            }
            else
            {
                firstIndexes ??= FirstIndexLookup<T>.Create(actualSnapshot.Items, duplicateIndex, comparer, duplicateIndex);
                if (firstIndexes.Add(item, duplicateIndex) >= 0)
                    return;
            }
        }

        // Every item was observed to find that they are all distinct, so they can be reported like a synchronous sequence.
        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<IReadOnlyList<T>>(nameof(NotDistinct), "all distinct items", actualSnapshot.Items, actualExpression, message)));
    }
}
