using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void Collection<T>(IEnumerable<T> actual, Action<T>[] inspectors, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        EnsureComplete(actualSnapshot);
        if (actualSnapshot.Items.Count != inspectors.Length)
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionAssertionError<T>(actualSnapshot, inspectors.Length, actualExpression, message)));
        }

        for (var i = 0; i < inspectors.Length; i++)
        {
            try
            {
                inspectors[i](actualSnapshot.Items[i]);
            }
            catch (Exception exception)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionInspectorAssertionError<T>(actualSnapshot, i, exception, actualExpression, message)), exception);
            }
        }
    }

    public static void Collection<T>(IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, Action<T> inspector13, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12, inspector13], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, Action<T> inspector13, Action<T> inspector14, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12, inspector13, inspector14], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, Action<T> inspector13, Action<T> inspector14, Action<T> inspector15, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12, inspector13, inspector14, inspector15], message, actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, Action<T> inspector13, Action<T> inspector14, Action<T> inspector15, Action<T> inspector16, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12, inspector13, inspector14, inspector15, inspector16], message, actualExpression);
    }
}
