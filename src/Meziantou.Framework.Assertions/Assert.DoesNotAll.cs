using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that not all items in an enumerable satisfy the specified predicate (i.e., at least one item does not).</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate that at least one item must not satisfy.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    public static void DoesNotAll<T>(IEnumerable<T> actual, Func<T, bool> predicate, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);

        foreach (var item in actualSnapshot)
        {
            if (!predicate(item))
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new CollectionDoesNotAllPredicateAssertionError<T>(actualSnapshot, actualExpression, predicateExpression)));
    }
}
