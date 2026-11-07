namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A wrapped function exotic object (https://tc39.es/proposal-shadowrealm/#sec-wrapped-function-exotic-objects): a
/// function of a realm that calls a function of another realm across the callable boundary of a ShadowRealm. Only the
/// primitive values and the callable objects (wrapped again) cross the boundary.
/// </summary>
internal sealed class JsWrappedFunction : JsFunction
{
    private JsWrappedFunction(JsRealm callerRealm, JsObject target)
        : base(callerRealm, callerRealm.Intrinsics.FunctionPrototype)
    {
        WrappedTargetFunction = target;
    }

    /// <summary>Gets the [[WrappedTargetFunction]].</summary>
    public JsObject WrappedTargetFunction { get; }

    /// <summary>WrappedFunctionCreate(callerRealm, Target)</summary>
    public static JsWrappedFunction Create(JsRealm callerRealm, JsObject target)
    {
        var wrapped = new JsWrappedFunction(callerRealm, target);
        try
        {
            FunctionBuiltins.CopyNameAndLength(wrapped, target);
        }
        catch (JsException)
        {
            ThrowHelper.ThrowTypeError("The function cannot be wrapped: reading its length or its name threw an exception");
        }

        return wrapped;
    }

    /// <summary>GetWrappedValue(callerRealm, value): a primitive value, or a wrapped function of the realm for a callable object.</summary>
    public static JsValue GetWrappedValue(JsRealm callerRealm, JsValue value)
    {
        if (value.Cell is JsObject obj)
        {
            if (!obj.IsCallable)
                return ThrowHelper.ThrowTypeError<JsValue>("Only primitive values and callable objects can cross the boundary of a ShadowRealm");

            return Create(callerRealm, obj);
        }

        return value;
    }

    /// <summary>
    /// CreateTypeErrorCopy(realmRecord, originalError): the TypeError of the realm that replaces an exception that crosses
    /// the boundary. No JavaScript code runs: the message is only read from an own data property of an error object.
    /// </summary>
    public static JsException CreateTypeErrorCopy(JsRealm realm, JsValue originalError)
    {
        string detail;
        if (originalError.Cell is JsErrorObject error && error.GetOwnProperty("message", out var descriptor) && descriptor.IsDataDescriptor && descriptor.Value.Cell is JsString message)
        {
            detail = message.ToString();
        }
        else
        {
            detail = JsOperations.DescribeForError(originalError);
        }

        return new JsException(new JsValue(realm.CreateError(ErrorType.TypeError, "An exception crossed the boundary of a ShadowRealm: " + detail)));
    }

    /// <summary>[[Call]](thisArgument, argumentsList) (https://tc39.es/proposal-shadowrealm/#sec-wrapped-function-exotic-objects-call-thisargument-argumentslist)</summary>
    internal override JsValue Call(JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        // The errors created by the call are associated with the realm of the wrapped function (the caller realm)
        var callerRealm = Realm;
        var engine = callerRealm.Engine;
        var previousRealm = engine.EnterNativeCall(callerRealm);
        try
        {
            var target = WrappedTargetFunction;
            var targetRealm = ObjectOperations.GetFunctionRealm(target);
            var wrappedArguments = arguments.Length == 0 ? [] : new JsValue[arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                wrappedArguments[i] = GetWrappedValue(targetRealm, arguments[i]);
            }

            var wrappedThisArgument = GetWrappedValue(targetRealm, thisValue);
            JsValue result;
            try
            {
                result = target.Call(wrappedThisArgument, wrappedArguments);
            }
            catch (JsException ex)
            {
                throw CreateTypeErrorCopy(callerRealm, ex.Value);
            }

            return GetWrappedValue(callerRealm, result);
        }
        finally
        {
            engine.ExitNativeCall(previousRealm);
        }
    }
}
