namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Error, the NativeError constructors (https://tc39.es/ecma262/#sec-error-objects).</summary>
internal static class ErrorBuiltins
{
    public static void Initialize(Intrinsics intrinsics, JsObject[] prototypes, JsObject[] constructors)
    {
        var realm = intrinsics.Realm;
        var errorPrototype = prototypes[(int)ErrorType.Error];
        var error = realm.CreateBuiltinFunction("Error", 1, (f, _, args, newTarget) => Construct(f, args, newTarget, ErrorType.Error), isConstructor: true);
        InitializeConstructor(error, errorPrototype);
        realm.DefineMethod(errorPrototype, "toString", 0, ToString);
        realm.DefineAccessor(errorPrototype, "stack", GetStack, SetStack);
        constructors[(int)ErrorType.Error] = error;
        intrinsics.AddGlobal("Error", error);

        foreach (var type in Enum.GetValues<ErrorType>())
        {
            if (type is ErrorType.Error or ErrorType.AggregateError)
                continue;

            var prototype = prototypes[(int)type];
            prototype.SetPrototypeDirect(errorPrototype);
            var constructor = realm.CreateBuiltinFunction(type.ToString(), 1, (f, _, args, newTarget) => Construct(f, args, newTarget, type), isConstructor: true, prototype: error);
            InitializeConstructor(constructor, prototype);
            constructors[(int)type] = constructor;
            intrinsics.AddGlobal(type.ToString(), constructor);
        }

        var aggregatePrototype = prototypes[(int)ErrorType.AggregateError];
        aggregatePrototype.SetPrototypeDirect(errorPrototype);
        var aggregate = realm.CreateBuiltinFunction("AggregateError", 2, ConstructAggregateError, isConstructor: true, prototype: error);
        InitializeConstructor(aggregate, aggregatePrototype);
        constructors[(int)ErrorType.AggregateError] = aggregate;
        intrinsics.AddGlobal("AggregateError", aggregate);
    }

    private static void InitializeConstructor(JsFunction constructor, JsObject prototype)
    {
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn("name", ((JsValue)constructor.Get(new PropertyKey("name"))).AsString());
        prototype.DefineBuiltIn("message", "");
    }

    private static JsValue Construct(JsBuiltinFunction function, ReadOnlySpan<JsValue> arguments, JsObject? newTarget, ErrorType type)
    {
        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, intrinsics => intrinsics.GetErrorPrototype(type));
        var error = new JsErrorObject(prototype);
        var message = arguments.Length > 0 ? arguments[0] : default;
        if (!message.IsUndefined)
        {
            ObjectOperations.CreateNonEnumerableDataPropertyOrThrow(error, "message", JsValue.FromJsString(JsOperations.ToJsString(message)));
        }

        InstallErrorCause(error, arguments.Length > 1 ? arguments[1] : default);
        error.CaptureStackTrace(function.Realm.Engine);
        return error;
    }

    private static JsValue ConstructAggregateError(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, intrinsics => intrinsics.GetErrorPrototype(ErrorType.AggregateError));
        var error = new JsErrorObject(prototype);
        var message = arguments.Length > 1 ? arguments[1] : default;
        if (!message.IsUndefined)
        {
            ObjectOperations.CreateNonEnumerableDataPropertyOrThrow(error, "message", JsValue.FromJsString(JsOperations.ToJsString(message)));
        }

        InstallErrorCause(error, arguments.Length > 2 ? arguments[2] : default);
        var errors = IteratorOperations.IterableToList(arguments.Length > 0 ? arguments[0] : default);
        ObjectOperations.CreateNonEnumerableDataPropertyOrThrow(error, "errors", JsArray.Create(function.Realm, errors.ToArray()));
        error.CaptureStackTrace(function.Realm.Engine);
        return error;
    }

    /// <summary>InstallErrorCause(O, options)</summary>
    private static void InstallErrorCause(JsObject error, JsValue options)
    {
        if (options.Cell is JsObject optionsObject && optionsObject.HasProperty(new PropertyKey("cause")))
        {
            var cause = optionsObject.Get(new PropertyKey("cause"));
            ObjectOperations.CreateNonEnumerableDataPropertyOrThrow(error, "cause", cause);
        }
    }

    /// <summary>get Error.prototype.stack (https://tc39.es/proposal-error-stack-accessor/)</summary>
    private static JsValue GetStack(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (!thisValue.IsObject)
            return ThrowHelper.ThrowTypeError<JsValue>("Error.prototype.stack getter called on non-object");

        return thisValue.Cell is JsErrorObject { StackTrace: { } stack } ? stack : JsValue.Undefined;
    }

    /// <summary>set Error.prototype.stack (https://tc39.es/proposal-error-stack-accessor/)</summary>
    private static JsValue SetStack(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsValue>("Error.prototype.stack setter called on non-object");

        if (arguments.Length == 0)
            return ThrowHelper.ThrowTypeError<JsValue>("Error.prototype.stack setter requires an argument");

        var value = arguments[0];
        if (!value.IsString)
            return ThrowHelper.ThrowTypeError<JsValue>("Error.prototype.stack must be a string");

        // SetterThatIgnoresPrototypeProperties(this, %Error.prototype%, "stack", value)
        if (ReferenceEquals(obj, function.Realm.Intrinsics.GetErrorPrototype(ErrorType.Error)))
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot set the stack of Error.prototype");

        var key = new PropertyKey("stack");
        if (!obj.GetOwnProperty(key, out _))
        {
            obj.CreateDataPropertyOrThrow(key, value);
        }
        else
        {
            obj.Set(key, value, throwOnFailure: true);
        }

        return JsValue.Undefined;
    }

    /// <summary>Error.prototype.toString()</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsValue>("Error.prototype.toString called on non-object");

        var name = obj.Get(new PropertyKey("name"));
        var nameString = name.IsUndefined ? "Error" : JsOperations.ToString(name);
        var message = obj.Get(new PropertyKey("message"));
        var messageString = message.IsUndefined ? "" : JsOperations.ToString(message);
        if (nameString.Length == 0)
            return messageString;

        if (messageString.Length == 0)
            return nameString;

        return nameString + ": " + messageString;
    }
}
