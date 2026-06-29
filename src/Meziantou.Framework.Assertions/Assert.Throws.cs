using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that the action throws an exception exactly of the specified type.</summary>
    /// <param name="action">The action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T Throws<T>(Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)Throws(typeof(T), action, actionExpression);
    }

    /// <summary>Asserts that the action throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    public static Exception Throws(Type expectedExceptionType, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCore(expectedExceptionType, allowDerivedTypes: false, action, actionExpression);
    }

    /// <summary>Asserts that the function throws an exception exactly of the specified type.</summary>
    /// <param name="action">The function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T Throws<T>(Func<object?> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)Throws(typeof(T), action, actionExpression);
    }

    /// <summary>Asserts that the function throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    public static Exception Throws(Type expectedExceptionType, Func<object?> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCore(expectedExceptionType, allowDerivedTypes: false, () => _ = action(), actionExpression);
    }

    /// <summary>Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> Throws<T>(Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await Throws(typeof(T), action, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    public static Task<Exception> Throws(Type expectedExceptionType, Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: false, action, actionExpression);
    }

    /// <summary>Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> Throws<T>(Func<Task<object?>> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await Throws(typeof(T), action, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    public static Task<Exception> Throws(Type expectedExceptionType, Func<Task<object?>> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: false, async () => _ = await action().ConfigureAwait(false), actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<T> ThrowsAsync<T>(Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return Throws<T>(action, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<Exception> ThrowsAsync(Type expectedExceptionType, Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return Throws(expectedExceptionType, action, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<T> ThrowsAsync<T>(Func<Task<object?>> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return Throws<T>(action, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<Exception> ThrowsAsync(Type expectedExceptionType, Func<Task<object?>> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return Throws(expectedExceptionType, action, actionExpression);
    }

    /// <summary>Asserts that the action throws an exception assignable to the specified type.</summary>
    /// <param name="action">The action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T ThrowsAny<T>(Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)ThrowsAny(typeof(T), action, actionExpression);
    }

    /// <summary>Asserts that the action throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    public static Exception ThrowsAny(Type expectedExceptionType, Action action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCore(expectedExceptionType, allowDerivedTypes: true, action, actionExpression);
    }

    /// <summary>Asserts that the function throws an exception assignable to the specified type.</summary>
    /// <param name="action">The function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T ThrowsAny<T>(Func<object?> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)ThrowsAny(typeof(T), action, actionExpression);
    }

    /// <summary>Asserts that the function throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    public static Exception ThrowsAny(Type expectedExceptionType, Func<object?> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCore(expectedExceptionType, allowDerivedTypes: true, () => _ = action(), actionExpression);
    }

    /// <summary>Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> ThrowsAny<T>(Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await ThrowsAny(typeof(T), action, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    public static Task<Exception> ThrowsAny(Type expectedExceptionType, Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: true, action, actionExpression);
    }

    /// <summary>Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> ThrowsAny<T>(Func<Task<object?>> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await ThrowsAny(typeof(T), action, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    public static Task<Exception> ThrowsAny(Type expectedExceptionType, Func<Task<object?>> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: true, async () => _ = await action().ConfigureAwait(false), actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<T> ThrowsAnyAsync<T>(Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return ThrowsAny<T>(action, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<Exception> ThrowsAnyAsync(Type expectedExceptionType, Func<Task> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsAny(expectedExceptionType, action, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<T> ThrowsAnyAsync<T>(Func<Task<object?>> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return ThrowsAny<T>(action, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Task<Exception> ThrowsAnyAsync(Type expectedExceptionType, Func<Task<object?>> action, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsAny(expectedExceptionType, action, actionExpression);
    }

    private static Exception ThrowsCore(Type expectedExceptionType, bool allowDerivedTypes, Action action, string? actionExpression)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            if (IsExpectedException(expectedExceptionType, allowDerivedTypes, exception))
                return exception;

            throw CreateThrowsException(expectedExceptionType, allowDerivedTypes, exception, actionExpression);
        }

        throw CreateThrowsException(expectedExceptionType, allowDerivedTypes, actualException: null, actionExpression);
    }

    private static async Task<Exception> ThrowsCoreAsync(Type expectedExceptionType, bool allowDerivedTypes, Func<Task> action, string? actionExpression)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (IsExpectedException(expectedExceptionType, allowDerivedTypes, exception))
                return exception;

            throw CreateThrowsException(expectedExceptionType, allowDerivedTypes, exception, actionExpression);
        }

        throw CreateThrowsException(expectedExceptionType, allowDerivedTypes, actualException: null, actionExpression);
    }

    private static AssertionException CreateThrowsException(Type expectedExceptionType, bool allowDerivedTypes, Exception? actualException, string? actionExpression)
    {
        return new AssertionException(ErrorFormatter.Format(new ThrowsAssertionError(expectedExceptionType, actualException, allowDerivedTypes, actionExpression)), actualException);
    }

    private static bool IsExpectedException(Type expectedExceptionType, bool allowDerivedTypes, Exception exception)
    {
        return allowDerivedTypes ? expectedExceptionType.IsAssignableFrom(exception.GetType()) : exception.GetType() == expectedExceptionType;
    }
}
