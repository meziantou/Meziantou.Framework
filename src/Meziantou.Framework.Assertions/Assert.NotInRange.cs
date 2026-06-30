using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotInRange<T>(T actual, T low, T high, IComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= Comparer<T>.Default;
        if (comparer.Compare(actual, low) < 0 || comparer.Compare(actual, high) > 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeRangeAssertionError<T>(nameof(NotInRange), actual, low, high, actualExpression, message)));
    }
}
