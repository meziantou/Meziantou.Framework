using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

// See Assert.Throws.cs for the overload resolution priorities of the delegate overloads
public partial class Assert
{
    public static void DoesNotThrow(Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (!IsXunitSkipException(ex))
        {
            throw CreateDoesNotThrowException(nameof(DoesNotThrow), "exception", ex, message, actionExpression);
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
        catch (Exception ex) when (ex.GetType() == exceptionType && !IsXunitSkipException(ex))
        {
            throw CreateDoesNotThrowException(nameof(DoesNotThrow), "exception of type " + exceptionType.FullName, ex, message, actionExpression);
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
        catch (Exception ex) when (exceptionType.IsAssignableFrom(ex.GetType()) && !IsXunitSkipException(ex))
        {
            throw CreateDoesNotThrowException(nameof(DoesNotThrowAny), "exception assignable to " + exceptionType.FullName, ex, message, actionExpression);
        }
    }

    [OverloadResolutionPriority(1)]
    public static async Task DoesNotThrow(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsXunitSkipException(ex))
        {
            throw CreateDoesNotThrowException(nameof(DoesNotThrow), "exception", ex, message, actionExpression);
        }
    }

    [OverloadResolutionPriority(1)]
    public static async Task DoesNotThrow<T>(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        await DoesNotThrow(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    [OverloadResolutionPriority(1)]
    public static async Task DoesNotThrow(Type exceptionType, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.GetType() == exceptionType && !IsXunitSkipException(ex))
        {
            throw CreateDoesNotThrowException(nameof(DoesNotThrow), "exception of type " + exceptionType.FullName, ex, message, actionExpression);
        }
    }

    [OverloadResolutionPriority(1)]
    public static async Task DoesNotThrowAny<T>(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        await DoesNotThrowAny(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    [OverloadResolutionPriority(1)]
    public static async Task DoesNotThrowAny(Type exceptionType, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (exceptionType.IsAssignableFrom(ex.GetType()) && !IsXunitSkipException(ex))
        {
            throw CreateDoesNotThrowException(nameof(DoesNotThrowAny), "exception assignable to " + exceptionType.FullName, ex, message, actionExpression);
        }
    }

    public static async Task DoesNotThrow(Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        await DoesNotThrow(async () => await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    public static async Task DoesNotThrow<T>(Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        await DoesNotThrow(typeof(T), async () => await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    public static async Task DoesNotThrow(Type exceptionType, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        await DoesNotThrow(exceptionType, async () => await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    public static async Task DoesNotThrowAny<T>(Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        await DoesNotThrowAny(typeof(T), async () => await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    public static async Task DoesNotThrowAny(Type exceptionType, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        await DoesNotThrowAny(exceptionType, async () => await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    private static AssertionException CreateDoesNotThrowException(string assertionName, string notExpected, Exception exception, string? message, string? actionExpression)
    {
        return new AssertionException(ErrorFormatter.Format(new NegativeExceptionAssertionError(assertionName, notExpected, actionExpression ?? "<action>", exception.GetType(), exception.Message, message)), exception);
    }
}
