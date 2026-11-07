namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The stack trace API of V8 (https://v8.dev/docs/stack-trace-api), enabled by <see cref="JsEngineOptions.EnableV8StackTraceApi"/>:
/// <c>Error.captureStackTrace</c> and <c>Error.stackTraceLimit</c>. They are not part of ECMAScript.
/// </summary>
internal static class ErrorStackTraceApi
{
    /// <summary>The default value of <c>Error.stackTraceLimit</c>, and the number of frames of a stack trace when the API is not enabled.</summary>
    public const int DefaultStackTraceLimit = 10;

    private static readonly PropertyKey StackTraceLimitKey = new("stackTraceLimit");

    public static void Initialize(JsRealm realm, JsObject errorConstructor)
    {
        realm.DefineMethod(errorConstructor, "captureStackTrace", 2, CaptureStackTrace);
        errorConstructor.DefineBuiltIn(StackTraceLimitKey, DefaultStackTraceLimit, PropertyFlags.Default);
    }

    /// <summary>
    /// Gets the number of frames of the stack traces of the realm: <c>Error.stackTraceLimit</c> when the API is enabled (0
    /// when it is not a number), <see cref="DefaultStackTraceLimit"/> otherwise. The property is read without running code.
    /// </summary>
    public static int GetStackTraceLimit(JsRealm? realm)
    {
        if (realm is null || !realm.Engine.Options.EnableV8StackTraceApi)
            return DefaultStackTraceLimit;

        var constructor = realm.Intrinsics.GetErrorConstructor(ErrorType.Error);
        if (!constructor.GetOwnProperty(StackTraceLimitKey, out var descriptor) || !descriptor.IsDataDescriptor || !descriptor.Value.IsNumber)
            return 0;

        var limit = descriptor.Value.NumberValue;
        return double.IsNaN(limit) || limit <= 0 ? 0 : limit >= int.MaxValue ? int.MaxValue : (int)limit;
    }

    /// <summary>
    /// Error.captureStackTrace(targetObject[, constructorOpt]): defines the <c>stack</c> own property of the object (writable,
    /// configurable, not enumerable). The frames above the last call of <c>constructorOpt</c>, and this call, are omitted
    /// (no frame when the function is not on the stack).
    /// </summary>
    private static JsValue CaptureStackTrace(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if ((arguments.Length > 0 ? arguments[0] : default).Cell is not JsObject target)
            return ThrowHelper.ThrowTypeError<JsValue>("Error.captureStackTrace: the target must be an object");

        var constructorOpt = arguments.Length > 1 ? arguments[1].Cell as JsObject : null;
        // The first line is the one of Error.prototype.toString: "name: message", with "Error" as the default name
        var name = target.Get(new PropertyKey("name"));
        var nameString = name.IsUndefined ? "Error" : JsOperations.ToString(name);
        var message = target.Get(new PropertyKey("message"));
        var messageString = message.IsUndefined ? "" : JsOperations.ToString(message);
        var header = nameString.Length == 0 ? messageString : messageString.Length == 0 ? nameString : nameString + ": " + messageString;
        var stack = JsErrorObject.FormatStackTrace(header, function.Realm.Engine, GetStackTraceLimit(function.Realm), constructorOpt);
        JsErrorObject.TrySetStackTrace(target, stack);
        target.DefinePropertyOrThrow(new PropertyKey("stack"), PropertyDescriptor.Data(JsValue.FromString(stack), PropertyFlags.Writable | PropertyFlags.Configurable));
        return JsValue.Undefined;
    }
}
