using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void Collection<T>(IEnumerable<T> actual, Action<T>[] inspectors, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        EnsureComplete(actualSnapshot);
        if (actualSnapshot.Items.Count != inspectors.Length)
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionAssertionError<T>(actualSnapshot, inspectors.Length, actualExpression)));
        }

        for (var i = 0; i < inspectors.Length; i++)
        {
            try
            {
                inspectors[i](actualSnapshot.Items[i]);
            }
            catch (Exception exception)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionInspectorAssertionError<T>(actualSnapshot, i, exception, actualExpression)), exception);
            }
        }
    }

    public static void Collection<T>(IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, Action<T> inspector13, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12, inspector13], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, Action<T> inspector13, Action<T> inspector14, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12, inspector13, inspector14], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, Action<T> inspector13, Action<T> inspector14, Action<T> inspector15, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12, inspector13, inspector14, inspector15], actualExpression);
    }

    public static void Collection<T>(IEnumerable<T> actual, Action<T> inspector1, Action<T> inspector2, Action<T> inspector3, Action<T> inspector4, Action<T> inspector5, Action<T> inspector6, Action<T> inspector7, Action<T> inspector8, Action<T> inspector9, Action<T> inspector10, Action<T> inspector11, Action<T> inspector12, Action<T> inspector13, Action<T> inspector14, Action<T> inspector15, Action<T> inspector16, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Collection(actual, [inspector1, inspector2, inspector3, inspector4, inspector5, inspector6, inspector7, inspector8, inspector9, inspector10, inspector11, inspector12, inspector13, inspector14, inspector15, inspector16], actualExpression);
    }
}
