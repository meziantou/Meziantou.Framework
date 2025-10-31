using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

/// <summary>Represents an awaitable wrapper that can be used with the await pattern.</summary>
/// <remarks>
/// This type wraps an arbitrary awaitable object and provides a uniform way to await it.
/// It is designed to work with any type that implements the awaitable pattern, including
/// Task, ValueTask, and custom awaitable types.
/// </remarks>
public readonly struct ObjectMethodExecutorAwaitable
{
    private readonly object _customAwaitable;
    private readonly Func<object, object> _getAwaiterMethod;
    private readonly Func<object, bool> _isCompletedMethod;
    private readonly Func<object, object?> _getResultMethod;
    private readonly Action<object, Action> _onCompletedMethod;
    private readonly Action<object, Action> _unsafeOnCompletedMethod;

    // Perf note: since we're requiring the customAwaitable to be supplied here as an object,
    // this will trigger a further allocation if it was a value type (i.e., to box it). We can't
    // fix this by making the customAwaitable type generic, because the calling code typically
    // does not know the type of the awaitable/awaiter at compile-time anyway.
    //
    // However, we could fix it by not passing the customAwaitable here at all, and instead
    // passing a func that maps directly from the target object (e.g., controller instance),
    // target method (e.g., action method info), and params array to the custom awaiter in the
    // GetAwaiter() method below. In effect, by delaying the actual method call until the
    // upstream code calls GetAwaiter on this ObjectMethodExecutorAwaitable instance.
    // This optimization is not currently implemented because:
    // [1] It would make no difference when the awaitable was an object type, which is
    //     by far the most common scenario (e.g., System.Task<T>).
    // [2] It would be complex - we'd need some kind of object pool to track all the parameter
    //     arrays until we needed to use them in GetAwaiter().
    // We can reconsider this in the future if there's a need to optimize for ValueTask<T>
    // or other value-typed awaitables.

    internal ObjectMethodExecutorAwaitable(
        object customAwaitable,
        Func<object, object> getAwaiterMethod,
        Func<object, bool> isCompletedMethod,
        Func<object, object?> getResultMethod,
        Action<object, Action> onCompletedMethod,
        Action<object, Action> unsafeOnCompletedMethod)
    {
        _customAwaitable = customAwaitable;
        _getAwaiterMethod = getAwaiterMethod;
        _isCompletedMethod = isCompletedMethod;
        _getResultMethod = getResultMethod;
        _onCompletedMethod = onCompletedMethod;
        _unsafeOnCompletedMethod = unsafeOnCompletedMethod;
    }

    /// <summary>Returns the awaiter for this awaitable.</summary>
    /// <returns>An awaiter that can be used to await this awaitable.</returns>
    public Awaiter GetAwaiter()
    {
        var customAwaiter = _getAwaiterMethod(_customAwaitable);
        return new Awaiter(customAwaiter, _isCompletedMethod, _getResultMethod, _onCompletedMethod, _unsafeOnCompletedMethod);
    }

    /// <summary>Provides an awaiter that supports the await pattern.</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "")]
    public readonly struct Awaiter : ICriticalNotifyCompletion
    {
        private readonly object _customAwaiter;
        private readonly Func<object, bool> _isCompletedMethod;
        private readonly Func<object, object?> _getResultMethod;
        private readonly Action<object, Action> _onCompletedMethod;
        private readonly Action<object, Action> _unsafeOnCompletedMethod;

        public Awaiter(
            object customAwaiter,
            Func<object, bool> isCompletedMethod,
            Func<object, object?> getResultMethod,
            Action<object, Action> onCompletedMethod,
            Action<object, Action> unsafeOnCompletedMethod)
        {
            _customAwaiter = customAwaiter;
            _isCompletedMethod = isCompletedMethod;
            _getResultMethod = getResultMethod;
            _onCompletedMethod = onCompletedMethod;
            _unsafeOnCompletedMethod = unsafeOnCompletedMethod;
        }

        /// <summary>Gets a value indicating whether the asynchronous operation has completed.</summary>
        public bool IsCompleted => _isCompletedMethod(_customAwaiter);

        /// <summary>Retrieves the result of the asynchronous operation.</summary>
        /// <returns>The result of the asynchronous operation.</returns>
        public object? GetResult() => _getResultMethod(_customAwaiter);

        /// <summary>Schedules the continuation action to be invoked when the asynchronous operation completes.</summary>
        /// <param name="continuation">The action to invoke when the operation completes.</param>
        public void OnCompleted(Action continuation)
        {
            _onCompletedMethod(_customAwaiter, continuation);
        }

        /// <summary>Schedules the continuation action without capturing the execution context.</summary>
        /// <param name="continuation">The action to invoke when the operation completes.</param>
        public void UnsafeOnCompleted(Action continuation)
        {
            // If the underlying awaitable implements ICriticalNotifyCompletion, use its UnsafeOnCompleted.
            // If not, fall back on using its OnCompleted.
            //
            // Why this is safe:
            // - Implementing ICriticalNotifyCompletion is a way of saying the caller can choose whether it
            //   needs the execution context to be preserved (which it signals by calling OnCompleted), or
            //   that it doesn't (which it signals by calling UnsafeOnCompleted). Obviously it's faster *not*
            //   to preserve and restore the context, so we prefer that where possible.
            // - If a caller doesn't need the execution context to be preserved and hence calls UnsafeOnCompleted,
            //   there's no harm in preserving it anyway - it's just a bit of wasted cost. That's what will happen
            //   if a caller sees that the proxy implements ICriticalNotifyCompletion but the proxy chooses to
            //   pass the call on to the underlying awaitable's OnCompleted method.

            var underlyingMethodToUse = _unsafeOnCompletedMethod ?? _onCompletedMethod;
            underlyingMethodToUse(_customAwaiter, continuation);
        }
    }
}