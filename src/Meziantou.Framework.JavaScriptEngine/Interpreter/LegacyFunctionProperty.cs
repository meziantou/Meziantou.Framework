namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The <c>caller</c> or <c>arguments</c> own property of a sloppy function: an extension the spec allows, that the browsers
/// implement. It is a non-configurable accessor whose getter reads the running frames (a data property could not change its
/// value: it is not writable nor configurable), without setter (an assignment fails, a TypeError in strict code). The
/// accessor, and its getter, are shared by the sloppy functions of a realm.
/// </summary>
internal sealed class LegacyFunctionProperty(JsRealm realm, bool isCaller) : AccessorPair
{
    public override JsValue Getter
    {
        get
        {
            if (GetterValue.IsUndefined)
            {
                GetterValue = isCaller
                    ? realm.CreateBuiltinFunction("caller", 0, (_, thisValue, _, _) => thisValue.Cell is JsClosure function ? GetCaller(function) : JsValue.Null, prefix: "get")
                    : realm.CreateBuiltinFunction("arguments", 0, (_, thisValue, _, _) => thisValue.Cell is JsClosure function ? GetArguments(function) : JsValue.Null, prefix: "get");
            }

            return GetterValue;
        }
    }

    /// <summary>
    /// The function that made the innermost running call of <paramref name="function"/>, or null when the function is not
    /// running. The built-in functions have no frame: the caller is the function that called them. Only a sloppy function
    /// of the same realm is revealed: never a strict function, a class, a method, an arrow function, a generator, an async
    /// function or the code of a script (null). The frames of eval code are skipped: the caller of a function called by eval
    /// code is the function that runs the eval.
    /// </summary>
    private static JsValue GetCaller(JsClosure function)
    {
        if (!function.HasLegacyCallerAndArguments)
            return JsValue.Null;

        var frames = function.Realm.Engine.Frames;
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(frames[i].Function, function))
                continue;

            var callerIndex = i - 1;
            while (callerIndex >= 0 && (frames[callerIndex].Code.Flags & Compiler.FunctionTemplateFlags.EvalCode) != 0)
            {
                callerIndex--;
            }

            if (callerIndex >= 0 && frames[callerIndex].Function is { } caller && caller.HasLegacyCallerAndArguments && ReferenceEquals(caller.Realm, function.Realm))
                return caller;

            return JsValue.Null;
        }

        return JsValue.Null;
    }

    /// <summary>A new arguments object of the innermost running call of <paramref name="function"/>, or null when the function is not running.</summary>
    private static JsValue GetArguments(JsClosure function)
    {
        if (!function.HasLegacyCallerAndArguments)
            return JsValue.Null;

        var frames = function.Realm.Engine.Frames;
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            var frame = frames[i];
            if (ReferenceEquals(frame.Function, function))
                return JsInterpreter.CreateArgumentsCopy(frame);
        }

        return JsValue.Null;
    }
}
