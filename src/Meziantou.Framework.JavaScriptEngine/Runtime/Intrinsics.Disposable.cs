namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%SuppressedError%</summary>
    public JsFunction SuppressedError { get; private set; } = null!;

    /// <summary>%SuppressedError.prototype%</summary>
    public JsObject SuppressedErrorPrototype { get; private set; } = null!;

    /// <summary>%DisposableStack%</summary>
    public JsFunction DisposableStack { get; private set; } = null!;

    /// <summary>%DisposableStack.prototype%</summary>
    public JsObject DisposableStackPrototype { get; private set; } = null!;

    /// <summary>%AsyncDisposableStack%</summary>
    public JsFunction AsyncDisposableStack { get; private set; } = null!;

    /// <summary>%AsyncDisposableStack.prototype%</summary>
    public JsObject AsyncDisposableStackPrototype { get; private set; } = null!;

    /// <summary>
    /// The explicit resource management built-ins (SuppressedError, DisposableStack, AsyncDisposableStack,
    /// %AsyncIteratorPrototype%[@@asyncDispose]), then the ES2026 additions to the earlier built-ins. It runs after
    /// InitializeArray and InitializePromise.
    /// </summary>
    partial void InitializeDisposable()
    {
        SuppressedErrorPrototype = new JsObject(GetErrorPrototype(ErrorType.Error));
        SuppressedError = SuppressedErrorBuiltins.Initialize(this, SuppressedErrorPrototype);

        DisposableStackPrototype = new JsObject(ObjectPrototype);
        DisposableStack = DisposableStackBuiltins.Initialize(this, DisposableStackPrototype);

        AsyncDisposableStackPrototype = new JsObject(ObjectPrototype);
        AsyncDisposableStack = AsyncDisposableStackBuiltins.Initialize(this, AsyncDisposableStackPrototype);

        AsyncIteratorDisposeBuiltins.Initialize(this);

        InitializeEs2026Additions();
    }

    /// <summary>The ES2026 methods added to the earlier built-ins (Error.isError, Array.fromAsync).</summary>
    partial void InitializeEs2026Additions();
}
