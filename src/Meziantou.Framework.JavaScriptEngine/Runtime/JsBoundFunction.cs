namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A bound function exotic object (https://tc39.es/ecma262/#sec-bound-function-exotic-objects), created by
/// <c>Function.prototype.bind</c>. It is not a <see cref="JsFunction"/>: it has no [[Realm]] (GetFunctionRealm uses the
/// realm of the target).
/// </summary>
internal sealed class JsBoundFunction : JsObject, IBoundFunction
{
    private readonly JsEngine _engine;

    private JsBoundFunction(JsEngine engine, JsObject? prototype, JsObject target, JsValue boundThis, JsValue[] boundArguments)
        : base(prototype, ObjectClass.Function)
    {
        _engine = engine;
        BoundTargetFunction = target;
        BoundThis = boundThis;
        BoundArguments = boundArguments;
        Flags |= ObjectFlags.Callable;
        if (target.IsConstructor)
        {
            Flags |= ObjectFlags.Constructor;
        }
    }

    /// <summary>Gets the [[BoundTargetFunction]].</summary>
    public JsObject BoundTargetFunction { get; }

    /// <summary>Gets the [[BoundThis]].</summary>
    public JsValue BoundThis { get; }

    /// <summary>Gets the [[BoundArguments]].</summary>
    public JsValue[] BoundArguments { get; }

    /// <summary>BoundFunctionCreate(targetFunction, boundThis, boundArgs)</summary>
    public static JsBoundFunction Create(JsEngine engine, JsObject target, JsValue boundThis, JsValue[] boundArguments)
    {
        var prototype = target.GetPrototypeOf();
        return new JsBoundFunction(engine, prototype, target, boundThis, boundArguments);
    }

    internal override JsValue Call(JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        _engine.EnterRecursion();
        try
        {
            if (BoundArguments.Length == 0)
                return BoundTargetFunction.Call(BoundThis, arguments);

            return BoundTargetFunction.Call(BoundThis, ConcatArguments(arguments));
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    internal override JsValue Construct(ReadOnlySpan<JsValue> arguments, JsObject newTarget)
    {
        _engine.EnterRecursion();
        try
        {
            if (ReferenceEquals(newTarget, this))
            {
                newTarget = BoundTargetFunction;
            }

            var args = BoundArguments.Length == 0 ? arguments : ConcatArguments(arguments);
            return BoundTargetFunction.Construct(args, newTarget);
        }
        finally
        {
            _engine.ExitRecursion();
        }
    }

    private JsValue[] ConcatArguments(ReadOnlySpan<JsValue> arguments)
    {
        var args = new JsValue[BoundArguments.Length + arguments.Length];
        BoundArguments.CopyTo(args, 0);
        arguments.CopyTo(args.AsSpan(BoundArguments.Length));
        return args;
    }
}
