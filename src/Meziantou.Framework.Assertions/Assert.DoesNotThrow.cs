using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotThrow(Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrow), "exception", actionExpression ?? "<action>", ex.GetType(), ex.Message, message)));
        }
    }

    public static void DoesNotThrow<T>(Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        DoesNotThrow(typeof(T), action, message, actionExpression);
    }

    public static void DoesNotThrow(Type exceptionType, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex.GetType() == exceptionType)
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrow), "exception of type " + exceptionType.FullName, actionExpression ?? "<action>", ex.GetType(), ex.Message, message)));
        }
    }

    public static void DoesNotThrowAny<T>(Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        DoesNotThrowAny(typeof(T), action, message, actionExpression);
    }

    public static void DoesNotThrowAny(Type exceptionType, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (exceptionType.IsAssignableFrom(ex.GetType()))
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrowAny), "exception assignable to " + exceptionType.FullName, actionExpression ?? "<action>", ex.GetType(), ex.Message, message)));
        }
    }

    public static async Task DoesNotThrow(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrow), "exception", actionExpression ?? "<action>", ex.GetType(), ex.Message, message)));
        }
    }

    public static async Task DoesNotThrow<T>(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        await DoesNotThrow(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    public static async Task DoesNotThrow(Type exceptionType, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.GetType() == exceptionType)
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrow), "exception of type " + exceptionType.FullName, actionExpression ?? "<action>", ex.GetType(), ex.Message, message)));
        }
    }

    public static async Task DoesNotThrowAny<T>(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        await DoesNotThrowAny(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    public static async Task DoesNotThrowAny(Type exceptionType, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (exceptionType.IsAssignableFrom(ex.GetType()))
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeExceptionAssertionError(nameof(DoesNotThrowAny), "exception assignable to " + exceptionType.FullName, actionExpression ?? "<action>", ex.GetType(), ex.Message, message)));
        }
    }
}
