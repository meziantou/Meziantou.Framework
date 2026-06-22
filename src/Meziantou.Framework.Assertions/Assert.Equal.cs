using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void Equal<T>(T expected, T actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new EqualAssertionError<T, T>(expected, actual, message, actualExpression, expectedExpression)));
        }
    }

    public static void Equal<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Length != actual.Length)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanLengthAssertionError<T, T>(expected, actual, message, actualExpression, expectedExpression)));
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanEqualAssertionError<T, T>(expected, actual, i, message, actualExpression, expectedExpression)));
            }
        }
    }

    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();

        var index = 0;
        while (true)
        {
            var actualHasNext = actualEnumerator.MoveNext();
            var expectedHasNext = expectedEnumerator.MoveNext();

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            if (!EqualityComparer<T>.Default.Equals(expectedEnumerator.Current, actualEnumerator.Current))
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            index++;
        }
    }
}
