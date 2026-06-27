using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotThrow(Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrow), "exception", actionExpression ?? "<action>", ex.GetType(), ex.Message)));
        }
    }

    public static void DoesNotThrow<T>(Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        DoesNotThrow(typeof(T), action, actionExpression);
    }

    public static void DoesNotThrow(Type exceptionType, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex.GetType() == exceptionType)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrow), "exception of type " + exceptionType.FullName, actionExpression ?? "<action>", ex.GetType(), ex.Message)));
        }
    }

    public static void DoesNotThrowAny<T>(Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        DoesNotThrowAny(typeof(T), action, actionExpression);
    }

    public static void DoesNotThrowAny(Type exceptionType, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (exceptionType.IsAssignableFrom(ex.GetType()))
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrowAny), "exception assignable to " + exceptionType.FullName, actionExpression ?? "<action>", ex.GetType(), ex.Message)));
        }
    }

    public static async Task DoesNotThrow(Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrow), "exception", actionExpression ?? "<action>", ex.GetType(), ex.Message)));
        }
    }

    public static Task DoesNotThrow<T>(Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return DoesNotThrow(typeof(T), action, actionExpression);
    }

    public static async Task DoesNotThrow(Type exceptionType, Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.GetType() == exceptionType)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrow), "exception of type " + exceptionType.FullName, actionExpression ?? "<action>", ex.GetType(), ex.Message)));
        }
    }

    public static Task DoesNotThrowAny<T>(Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return DoesNotThrowAny(typeof(T), action, actionExpression);
    }

    public static async Task DoesNotThrowAny(Type exceptionType, Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (exceptionType.IsAssignableFrom(ex.GetType()))
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrowAny), "exception assignable to " + exceptionType.FullName, actionExpression ?? "<action>", ex.GetType(), ex.Message)));
        }
    }
}
