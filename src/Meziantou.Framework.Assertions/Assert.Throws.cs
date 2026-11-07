using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    // Overload resolution of the exception assertions (Throws, ThrowsAny, DoesNotThrow, DoesNotThrowAny):
    // - An async lambda or a throw expression converts to both Func<Task> and Func<ValueTask>, which are unrelated
    //   delegate types, so the call would be ambiguous. The Task-based overloads have a higher priority to keep
    //   binding these call sites exactly as before the ValueTask overloads were added.
    // - The ValueTask-based overloads keep the default priority. A lambda returning a ValueTask also converts to
    //   Action (and Func<object?>), and the priority filter removes every lower-priority candidate before
    //   betterness is evaluated: giving them a lower priority would make Action win and drop the ValueTask.
    //   With the default priority, Func<ValueTask> is better than Action and Func<object?> for such a lambda.
    // ExceptionAssertionOverloadResolutionTests (analyzer tests) pins the overload each delegate shape binds to.

    /// <summary>Asserts that the action throws an exception exactly of the specified type.</summary>
    /// <param name="action">The action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T Throws<T>(Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)Throws(typeof(T), action, message, actionExpression);
    }

    /// <summary>Asserts that the action throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    public static Exception Throws(Type expectedExceptionType, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCore(expectedExceptionType, allowDerivedTypes: false, action, message, actionExpression);
    }

    /// <summary>Asserts that the function throws an exception exactly of the specified type.</summary>
    /// <param name="action">The function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T Throws<T>(Func<object?> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)Throws(typeof(T), action, message, actionExpression);
    }

    /// <summary>Asserts that the function throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    public static Exception Throws(Type expectedExceptionType, Func<object?> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCore(expectedExceptionType, allowDerivedTypes: false, () => _ = action(), message, actionExpression);
    }

    /// <summary>Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<T> Throws<T>(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await Throws(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<Exception> Throws(Type expectedExceptionType, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: false, action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<T> Throws<T>(Func<Task<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await Throws(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<Exception> Throws(Type expectedExceptionType, Func<Task<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: false, async () => _ = await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> Throws<T>(Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await Throws(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    public static async Task<Exception> Throws(Type expectedExceptionType, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: false, async () => await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> Throws<T>(Func<ValueTask<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await Throws(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    public static async Task<Exception> Throws(Type expectedExceptionType, Func<ValueTask<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: false, async () => _ = await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<T> ThrowsAsync<T>(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return await Throws<T>(action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous action throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<Exception> ThrowsAsync(Type expectedExceptionType, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await Throws(expectedExceptionType, action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<T> ThrowsAsync<T>(Func<Task<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return await Throws<T>(action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous function throws an exception exactly of the specified type.</summary>
    /// <param name="expectedExceptionType">The exact expected exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<Exception> ThrowsAsync(Type expectedExceptionType, Func<Task<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await Throws(expectedExceptionType, action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the action throws an exception exactly of the specified type, whose <see cref="ArgumentException.ParamName"/> is <paramref name="paramName"/>.</summary>
    /// <param name="paramName">The expected parameter name.</param>
    /// <param name="action">The action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T Throws<T>(string? paramName, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : ArgumentException
    {
        return CheckParameterName(Throws<T>(action, message, actionExpression), paramName, message, actionExpression);
    }

    /// <summary>Asserts that the function throws an exception exactly of the specified type, whose <see cref="ArgumentException.ParamName"/> is <paramref name="paramName"/>.</summary>
    /// <param name="paramName">The expected parameter name.</param>
    /// <param name="action">The function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T Throws<T>(string? paramName, Func<object?> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : ArgumentException
    {
        return CheckParameterName(Throws<T>(action, message, actionExpression), paramName, message, actionExpression);
    }

    /// <summary>Asserts that the asynchronous action throws an exception exactly of the specified type, whose <see cref="ArgumentException.ParamName"/> is <paramref name="paramName"/>.</summary>
    /// <param name="paramName">The expected parameter name.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<T> Throws<T>(string? paramName, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : ArgumentException
    {
        return CheckParameterName(await Throws<T>(action, message, actionExpression).ConfigureAwait(false), paramName, message, actionExpression);
    }

    /// <summary>Asserts that the asynchronous function throws an exception exactly of the specified type, whose <see cref="ArgumentException.ParamName"/> is <paramref name="paramName"/>.</summary>
    /// <param name="paramName">The expected parameter name.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<T> Throws<T>(string? paramName, Func<Task<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : ArgumentException
    {
        return CheckParameterName(await Throws<T>(action, message, actionExpression).ConfigureAwait(false), paramName, message, actionExpression);
    }

    /// <summary>Asserts that the asynchronous action throws an exception exactly of the specified type, whose <see cref="ArgumentException.ParamName"/> is <paramref name="paramName"/>.</summary>
    /// <param name="paramName">The expected parameter name.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> Throws<T>(string? paramName, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : ArgumentException
    {
        return CheckParameterName(await Throws<T>(action, message, actionExpression).ConfigureAwait(false), paramName, message, actionExpression);
    }

    /// <summary>Asserts that the asynchronous function throws an exception exactly of the specified type, whose <see cref="ArgumentException.ParamName"/> is <paramref name="paramName"/>.</summary>
    /// <param name="paramName">The expected parameter name.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> Throws<T>(string? paramName, Func<ValueTask<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : ArgumentException
    {
        return CheckParameterName(await Throws<T>(action, message, actionExpression).ConfigureAwait(false), paramName, message, actionExpression);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous action throws an exception exactly of the specified type, whose <see cref="ArgumentException.ParamName"/> is <paramref name="paramName"/>.</summary>
    /// <param name="paramName">The expected parameter name.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The exact expected exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<T> ThrowsAsync<T>(string? paramName, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : ArgumentException
    {
        return await Throws<T>(paramName, action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the action throws an exception assignable to the specified type.</summary>
    /// <param name="action">The action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T ThrowsAny<T>(Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)ThrowsAny(typeof(T), action, message, actionExpression);
    }

    /// <summary>Asserts that the action throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    public static Exception ThrowsAny(Type expectedExceptionType, Action action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCore(expectedExceptionType, allowDerivedTypes: true, action, message, actionExpression);
    }

    /// <summary>Asserts that the function throws an exception assignable to the specified type.</summary>
    /// <param name="action">The function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static T ThrowsAny<T>(Func<object?> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)ThrowsAny(typeof(T), action, message, actionExpression);
    }

    /// <summary>Asserts that the function throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    public static Exception ThrowsAny(Type expectedExceptionType, Func<object?> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return ThrowsCore(expectedExceptionType, allowDerivedTypes: true, () => _ = action(), message, actionExpression);
    }

    /// <summary>Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<T> ThrowsAny<T>(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await ThrowsAny(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<Exception> ThrowsAny(Type expectedExceptionType, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: true, action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<T> ThrowsAny<T>(Func<Task<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await ThrowsAny(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    [OverloadResolutionPriority(1)]
    public static async Task<Exception> ThrowsAny(Type expectedExceptionType, Func<Task<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: true, async () => _ = await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> ThrowsAny<T>(Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await ThrowsAny(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    public static async Task<Exception> ThrowsAny(Type expectedExceptionType, Func<ValueTask> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: true, async () => await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    public static async Task<T> ThrowsAny<T>(Func<ValueTask<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return (T)await ThrowsAny(typeof(T), action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    public static async Task<Exception> ThrowsAny(Type expectedExceptionType, Func<ValueTask<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsCoreAsync(expectedExceptionType, allowDerivedTypes: true, async () => _ = await action().ConfigureAwait(false), message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<T> ThrowsAnyAsync<T>(Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return await ThrowsAny<T>(action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous action throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous action expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the action.</param>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<Exception> ThrowsAnyAsync(Type expectedExceptionType, Func<Task> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsAny(expectedExceptionType, action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <typeparam name="T">The expected base exception type.</typeparam>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<T> ThrowsAnyAsync<T>(Func<Task<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
        where T : Exception
    {
        return await ThrowsAny<T>(action, message, actionExpression).ConfigureAwait(false);
    }

    /// <summary>Compatibility shim for xUnit. Asserts that the asynchronous function throws an exception assignable to the specified type.</summary>
    /// <param name="expectedExceptionType">The expected base exception type.</param>
    /// <param name="action">The asynchronous function expected to throw.</param>
    /// <param name="actionExpression">The expression that produced the function.</param>
    /// <returns>The thrown exception.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static async Task<Exception> ThrowsAnyAsync(Type expectedExceptionType, Func<Task<object?>> action, string? message = null, [CallerArgumentExpression(nameof(action))] string? actionExpression = null)
    {
        return await ThrowsAny(expectedExceptionType, action, message, actionExpression).ConfigureAwait(false);
    }

    private static Exception ThrowsCore(Type expectedExceptionType, bool allowDerivedTypes, Action action, string? message, string? actionExpression)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            if (IsUnexpectedXunitSkipException(expectedExceptionType, exception))
                throw;

            if (IsExpectedException(expectedExceptionType, allowDerivedTypes, exception))
                return exception;

            throw CreateThrowsException(expectedExceptionType, allowDerivedTypes, exception, message, actionExpression);
        }

        throw CreateThrowsException(expectedExceptionType, allowDerivedTypes, actualException: null, message, actionExpression);
    }

    private static async Task<Exception> ThrowsCoreAsync(Type expectedExceptionType, bool allowDerivedTypes, Func<Task> action, string? message, string? actionExpression)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (IsUnexpectedXunitSkipException(expectedExceptionType, exception))
                throw;

            if (IsExpectedException(expectedExceptionType, allowDerivedTypes, exception))
                return exception;

            throw CreateThrowsException(expectedExceptionType, allowDerivedTypes, exception, message, actionExpression);
        }

        throw CreateThrowsException(expectedExceptionType, allowDerivedTypes, actualException: null, message, actionExpression);
    }

    private static AssertionException CreateThrowsException(Type expectedExceptionType, bool allowDerivedTypes, Exception? actualException, string? message, string? actionExpression)
    {
        return new AssertionException(ErrorFormatter.Format(new ThrowsAssertionError(expectedExceptionType, actualException, allowDerivedTypes, actionExpression, message)), actualException);
    }

    private static bool IsExpectedException(Type expectedExceptionType, bool allowDerivedTypes, Exception exception)
    {
        return allowDerivedTypes ? expectedExceptionType.IsAssignableFrom(exception.GetType()) : exception.GetType() == expectedExceptionType;
    }

    // A skip request must escape the assertion unchanged, even when its type is assignable to the expected type
    // (ThrowsAny<Exception>). Only a caller that expects the exact type of the skip exception observes it.
    private static bool IsUnexpectedXunitSkipException(Type expectedExceptionType, Exception exception)
    {
        return IsXunitSkipException(exception) && exception.GetType() != expectedExceptionType;
    }

    private static T CheckParameterName<T>(T exception, string? expectedParamName, string? message, string? actionExpression)
        where T : ArgumentException
    {
        if (string.Equals(expectedParamName, exception.ParamName, StringComparison.Ordinal))
            return exception;

        throw new AssertionException(ErrorFormatter.Format(new ThrowsParameterNameAssertionError(exception, expectedParamName, actionExpression, message)), exception);
    }
}
