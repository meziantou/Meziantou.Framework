namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A realm (https://tc39.es/ecma262/#sec-code-realms): a global object, its global environment and the intrinsics.</summary>
public sealed class JsRealm
{
    internal JsRealm(JsEngine engine)
    {
        Engine = engine;
        var previousRealm = engine.CurrentRealm;
        engine.CurrentRealm = this;
        try
        {
            Intrinsics = new Intrinsics(this);
            GlobalObject = new JsObject(Intrinsics.ObjectPrototype);
            GlobalThis = new JsValue(GlobalObject);
            Intrinsics.Initialize();
            Intrinsics.DefineGlobalBindings(GlobalObject);
        }
        finally
        {
            engine.CurrentRealm = previousRealm ?? this;
        }
    }

    /// <summary>Gets the engine (the agent) of the realm.</summary>
    public JsEngine Engine { get; }

    /// <summary>Gets the global object ([[GlobalObject]]).</summary>
    public JsObject GlobalObject { get; }

    /// <summary>Gets the <c>this</c> value of the global code ([[GlobalThisValue]] of the global environment).</summary>
    public JsValue GlobalThis { get; }

    internal Intrinsics Intrinsics { get; }

    /// <summary>Evaluates a script in this realm, as an indirect <c>eval</c>, and returns its completion value.</summary>
    /// <exception cref="JsSyntaxErrorException">The code is not a valid script.</exception>
    /// <exception cref="JsException">The script threw an exception.</exception>
    public JsValue Evaluate(string code)
    {
        using var scope = Engine.Enter();
        var program = Parsing.JsParser.ParseScript(new Text.SourceText(code), Parsing.ParserOptions.Script);
        throw new NotSupportedException($"The compiler is not implemented yet ({program.Body.Count} statements)");
    }

    /// <summary>Creates a native error object of this realm.</summary>
    internal JsObject CreateError(ErrorType type, string message)
    {
        var error = new JsObject(Intrinsics.GetErrorPrototype(type), ObjectClass.Error);
        error.DefineBuiltIn("message", message);
        return error;
    }

    /// <summary>CreateBuiltinFunction: a function of this realm implemented by a .NET callback.</summary>
    internal JsBuiltinFunction CreateBuiltinFunction(PropertyKey name, int length, BuiltinFunctionCallback callback, bool isConstructor = false, JsObject? prototype = null, string? prefix = null)
    {
        var function = new JsBuiltinFunction(this, callback, isConstructor, prototype ?? Intrinsics.FunctionPrototype);
        function.SetFunctionLength(length);
        function.SetFunctionName(name, prefix);
        return function;
    }

    /// <summary>Defines a built-in method (writable, configurable, not enumerable).</summary>
    internal JsBuiltinFunction DefineMethod(JsObject target, PropertyKey name, int length, BuiltinFunctionCallback callback, PropertyFlags flags = PropertyFlags.BuiltIn)
    {
        var function = CreateBuiltinFunction(name, length, callback);
        target.DefineBuiltIn(name, function, flags);
        return function;
    }

    /// <summary>Defines a built-in accessor property; the getter is named "get name" and the setter "set name".</summary>
    internal void DefineAccessor(JsObject target, PropertyKey name, BuiltinFunctionCallback? getter, BuiltinFunctionCallback? setter, PropertyFlags flags = PropertyFlags.Configurable)
    {
        var getterFunction = getter is null ? null : CreateBuiltinFunction(name, 0, getter, prefix: "get");
        var setterFunction = setter is null ? null : CreateBuiltinFunction(name, 1, setter, prefix: "set");
        target.DefineBuiltInAccessor(name, getterFunction, setterFunction, flags);
    }
}
