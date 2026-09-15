using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that not all items in an enumerable satisfy the specified predicate (i.e., at least one item does not).</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate that at least one item must not satisfy.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    public static void DoesNotAll<T>([NotNull] IEnumerable<T>? actual, Func<T, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        if (actual is null)
            throw new AssertionException(ErrorFormatter.Format(new PredicateNullActualAssertionError(nameof(DoesNotAll), actualExpression, predicateExpression, message)));

        // Like !actual.All(predicate), an empty sequence fails: no item fails to satisfy the predicate.
        using var actualSnapshot = CollectionSnapshot.CreateSinglePass<T>(actual);

        for (var index = 0; actualSnapshot.TryGetItem(index, out var item); index++)
        {
            if (!predicate(item))
                return;
        }

        actualSnapshot.StopDiscardingItems();
        throw new AssertionException(ErrorFormatter.Format(new CollectionDoesNotAllPredicateAssertionError<T>(actualSnapshot, actualExpression, predicateExpression, message)));
    }
}
