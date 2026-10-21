namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsFunction? _disposableStack;
    private JsObject? _disposableStackPrototype;
    private JsFunction? _asyncDisposableStack;
    private JsObject? _asyncDisposableStackPrototype;
    private bool _isDisposableStacksInitializing;

    /// <summary>%SuppressedError%</summary>
    public JsFunction SuppressedError { get; private set; } = null!;

    /// <summary>%SuppressedError.prototype%</summary>
    public JsObject SuppressedErrorPrototype { get; private set; } = null!;

    /// <summary>%DisposableStack%</summary>
    public JsFunction DisposableStack => EnsureDisposableStacks()._disposableStack!;

    /// <summary>%DisposableStack.prototype%</summary>
    public JsObject DisposableStackPrototype => EnsureDisposableStacks()._disposableStackPrototype!;

    /// <summary>%AsyncDisposableStack%</summary>
    public JsFunction AsyncDisposableStack => EnsureDisposableStacks()._asyncDisposableStack!;

    /// <summary>%AsyncDisposableStack.prototype%</summary>
    public JsObject AsyncDisposableStackPrototype => EnsureDisposableStacks()._asyncDisposableStackPrototype!;

    /// <summary>
    /// The explicit resource management built-ins (SuppressedError, DisposableStack, AsyncDisposableStack,
    /// %AsyncIteratorPrototype%[@@asyncDispose]), then the ES2026 additions to the earlier built-ins. It runs after
    /// InitializeArray and InitializePromise. DisposableStack and AsyncDisposableStack are created on first use.
    /// </summary>
    partial void InitializeDisposable()
    {
        SuppressedErrorPrototype = new JsObject(GetErrorPrototype(ErrorType.Error));
        SuppressedError = SuppressedErrorBuiltins.Initialize(this, SuppressedErrorPrototype);
        AddLazyGlobal("DisposableStack", static intrinsics => intrinsics.DisposableStack);
        AddLazyGlobal("AsyncDisposableStack", static intrinsics => intrinsics.AsyncDisposableStack);
        AsyncIteratorDisposeBuiltins.Initialize(this);
        InitializeEs2026Additions();
    }

    /// <summary>The ES2026 methods added to the earlier built-ins (Error.isError, Array.fromAsync).</summary>
    partial void InitializeEs2026Additions();

    /// <summary>Creates the DisposableStack and AsyncDisposableStack built-ins, if they are not created yet.</summary>
    private Intrinsics EnsureDisposableStacks()
    {
        if (_disposableStack is null && !_isDisposableStacksInitializing)
        {
            _isDisposableStacksInitializing = true;
            _disposableStackPrototype = new JsObject(ObjectPrototype);
            _disposableStack = DisposableStackBuiltins.Initialize(this, _disposableStackPrototype);
            _asyncDisposableStackPrototype = new JsObject(ObjectPrototype);
            _asyncDisposableStack = AsyncDisposableStackBuiltins.Initialize(this, _asyncDisposableStackPrototype);
        }

        return this;
    }
}
