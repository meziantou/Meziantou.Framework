namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Function constructor and Function.prototype (https://tc39.es/ecma262/#sec-function-objects).</summary>
internal static class FunctionBuiltins
{
    private static readonly PropertyKey LengthKey = new("length");
    private static readonly PropertyKey NameKey = new("name");

    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.FunctionPrototype;
        prototype.SetFunctionLength(0);
        prototype.SetFunctionName("");

        var constructor = realm.CreateBuiltinFunction("Function", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);

        realm.DefineMethod(prototype, "apply", 2, Apply);
        realm.DefineMethod(prototype, "bind", 1, Bind);
        realm.DefineMethod(prototype, "call", 1, Call);
        realm.DefineMethod(prototype, "toString", 0, ToString);
        realm.DefineMethod(prototype, WellKnownSymbols.HasInstance, 1, HasInstance, PropertyFlags.None);

        // AddRestrictedFunctionProperties(%Function.prototype%, realm)
        var thrower = intrinsics.ThrowTypeError;
        prototype.DefineBuiltInAccessor("caller", thrower, thrower);
        prototype.DefineBuiltInAccessor("arguments", thrower, thrower);

        intrinsics.AddGlobal("Function", constructor);
        return constructor;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>Function(...parameterArgs, bodyArg)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return DynamicFunctions.CreateDynamicFunction(function, newTarget, DynamicFunctionKind.Normal, arguments);
    }

    /// <summary>Function.prototype.apply(thisArg, argArray)</summary>
    private static JsValue Apply(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject { IsCallable: true } func)
            return ThrowHelper.ThrowTypeError<JsValue>("Function.prototype.apply was called on a value that is not a function");

        var thisArg = Arg(arguments, 0);
        var argArray = Arg(arguments, 1);
        if (argArray.IsNullOrUndefined)
            return func.Call(thisArg, []);

        var argList = PropertyOperations.CreateListFromArrayLike(argArray);
        return func.Call(thisArg, argList);
    }

    /// <summary>Function.prototype.bind(thisArg, ...args)</summary>
    private static JsValue Bind(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject { IsCallable: true } target)
            return ThrowHelper.ThrowTypeError<JsValue>("Bind must be called on a function");

        var boundArguments = arguments.Length > 1 ? arguments[1..].ToArray() : [];
        var bound = JsBoundFunction.Create(function.Realm.Engine, target, Arg(arguments, 0), boundArguments);

        double length = 0;
        if (target.HasOwnProperty(LengthKey))
        {
            var targetLength = target.Get(LengthKey);
            if (targetLength.IsNumber)
            {
                var number = targetLength.NumberValue;
                if (double.IsPositiveInfinity(number))
                {
                    length = double.PositiveInfinity;
                }
                else if (!double.IsNegativeInfinity(number))
                {
                    var targetLengthAsInt = JsOperations.ToIntegerOrInfinity(targetLength);
                    length = Math.Max(targetLengthAsInt - boundArguments.Length, 0);
                }
            }
        }

        bound.DefineBuiltIn(LengthKey, length, PropertyFlags.Configurable);

        var targetName = target.Get(NameKey);
        var name = targetName.Cell is JsString nameString ? nameString.ToString() : "";
        bound.DefineBuiltIn(NameKey, "bound " + name, PropertyFlags.Configurable);
        return bound;
    }

    /// <summary>Function.prototype.call(thisArg, ...args)</summary>
    private static JsValue Call(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject { IsCallable: true } func)
            return ThrowHelper.ThrowTypeError<JsValue>("Function.prototype.call was called on a value that is not a function");

        return func.Call(Arg(arguments, 0), arguments.Length > 1 ? arguments[1..] : []);
    }

    /// <summary>Function.prototype[@@hasInstance](V)</summary>
    private static JsValue HasInstance(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ObjectOperations.OrdinaryHasInstance(thisValue, Arg(arguments, 0));
    }

    /// <summary>Function.prototype.toString()</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is JsFunction func)
        {
            var sourceText = func.GetSourceText();
            if (sourceText is not null)
                return sourceText;

            return GetNativeFunctionSource(func.InitialName);
        }

        if (thisValue.Cell is JsObject { IsCallable: true })
            return GetNativeFunctionSource(name: null);

        return ThrowHelper.ThrowTypeError<JsValue>("Function.prototype.toString requires that 'this' be a Function");
    }

    /// <summary>The NativeFunction representation of a function without source text: <c>function name() { [native code] }</c>.</summary>
    internal static string GetNativeFunctionSource(string? name)
    {
        if (name is null || !IsValidNativeFunctionName(name))
            return "function () { [native code] }";

        return "function " + name + "() { [native code] }";
    }

    // NativeFunctionAccessor? PropertyName: "get x", "set x", an identifier name or a computed name ("[Symbol.iterator]")
    private static bool IsValidNativeFunctionName(string name)
    {
        if (name.Length == 0)
            return true;

        var span = name.AsSpan();
        if (span.StartsWith("get ", StringComparison.Ordinal) || span.StartsWith("set ", StringComparison.Ordinal))
        {
            span = span[4..];
        }

        if (span.Length >= 2 && span[0] == '[' && span[^1] == ']')
            return span[1..^1].IndexOfAny('[', ']') < 0;

        if (span.Length == 0)
            return false;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            var valid = c is '$' or '_' || char.IsAsciiLetter(c) || (i > 0 && char.IsAsciiDigit(c));
            if (!valid)
                return false;
        }

        return true;
    }
}
