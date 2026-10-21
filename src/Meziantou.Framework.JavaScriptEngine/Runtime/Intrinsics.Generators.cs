using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The generators, the async functions, the async generators and eval.</summary>
internal sealed partial class Intrinsics
{
    private JsObject? _generatorFunctionPrototype;
    private JsObject? _generatorPrototype;
    private JsObject? _asyncFunctionPrototype;
    private JsObject? _asyncGeneratorFunctionPrototype;
    private JsObject? _asyncGeneratorPrototype;
    private JsObject? _asyncFromSyncIteratorPrototype;
    private JsFunction? _eval;

    /// <summary>%GeneratorFunction.prototype%: the prototype of the generator functions.</summary>
    public JsObject GeneratorFunctionPrototype => _generatorFunctionPrototype!;

    /// <summary>%GeneratorPrototype% (%GeneratorFunction.prototype.prototype%).</summary>
    public JsObject GeneratorPrototype => _generatorPrototype!;

    /// <summary>%AsyncFunction.prototype%</summary>
    public JsObject AsyncFunctionPrototype => _asyncFunctionPrototype!;

    /// <summary>%AsyncGeneratorFunction.prototype%</summary>
    public JsObject AsyncGeneratorFunctionPrototype => _asyncGeneratorFunctionPrototype!;

    /// <summary>%AsyncGeneratorPrototype%</summary>
    public JsObject AsyncGeneratorPrototype => _asyncGeneratorPrototype!;

    /// <summary>%AsyncFromSyncIteratorPrototype%</summary>
    public JsObject AsyncFromSyncIteratorPrototype => _asyncFromSyncIteratorPrototype!;

    /// <summary>Gets %Array.prototype.values% (the @@iterator of the arguments objects).</summary>
    public JsValue ArrayPrototypeValues { get; private set; }

    /// <summary>%eval%</summary>
    public JsFunction Eval => _eval!;

    /// <summary>Gets or sets the factory of the RegExp objects of the regular expression literals (set by the RegExp built-in).</summary>
    public Func<JsRealm, string, string, JsValue>? RegExpFactory { get; set; }

    partial void InitializeGenerators()
    {
        var realm = Realm;
        ArrayPrototypeValues = ArrayPrototype.GetOwnProperty("values", out var valuesDescriptor) ? valuesDescriptor.Value : default;

        // %GeneratorFunction%, %GeneratorFunction.prototype%, %GeneratorPrototype%
        _generatorFunctionPrototype = new JsObject(FunctionPrototype);
        _generatorPrototype = new JsObject(IteratorPrototype);
        var generatorFunction = CreateDynamicFunctionConstructor("GeneratorFunction", DynamicFunctionKind.Generator, _generatorFunctionPrototype);
        _generatorFunctionPrototype.DefineBuiltIn("prototype", _generatorPrototype, PropertyFlags.Configurable);
        _generatorFunctionPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "GeneratorFunction", PropertyFlags.Configurable);
        _generatorPrototype.DefineBuiltIn("constructor", _generatorFunctionPrototype, PropertyFlags.Configurable);
        realm.DefineMethod(_generatorPrototype, "next", 1, static (_, thisValue, arguments, _) => GetGenerator(thisValue).Resume(arguments.Length > 0 ? arguments[0] : default, kind: 0));
        realm.DefineMethod(_generatorPrototype, "return", 1, static (_, thisValue, arguments, _) => GetGenerator(thisValue).Resume(arguments.Length > 0 ? arguments[0] : default, kind: 2));
        realm.DefineMethod(_generatorPrototype, "throw", 1, static (_, thisValue, arguments, _) => GetGenerator(thisValue).Resume(arguments.Length > 0 ? arguments[0] : default, kind: 1));
        _generatorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Generator", PropertyFlags.Configurable);
        _ = generatorFunction;

        // %AsyncFunction%, %AsyncFunction.prototype%
        _asyncFunctionPrototype = new JsObject(FunctionPrototype);
        CreateDynamicFunctionConstructor("AsyncFunction", DynamicFunctionKind.Async, _asyncFunctionPrototype);
        _asyncFunctionPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "AsyncFunction", PropertyFlags.Configurable);

        // %AsyncIteratorPrototype%
        realm.DefineMethod(AsyncIteratorPrototype, WellKnownSymbols.AsyncIterator, 0, static (_, thisValue, _, _) => thisValue);

        // %AsyncGeneratorFunction%, %AsyncGeneratorFunction.prototype%, %AsyncGeneratorPrototype%
        _asyncGeneratorFunctionPrototype = new JsObject(FunctionPrototype);
        _asyncGeneratorPrototype = new JsObject(AsyncIteratorPrototype);
        CreateDynamicFunctionConstructor("AsyncGeneratorFunction", DynamicFunctionKind.AsyncGenerator, _asyncGeneratorFunctionPrototype);
        _asyncGeneratorFunctionPrototype.DefineBuiltIn("prototype", _asyncGeneratorPrototype, PropertyFlags.Configurable);
        _asyncGeneratorFunctionPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "AsyncGeneratorFunction", PropertyFlags.Configurable);
        _asyncGeneratorPrototype.DefineBuiltIn("constructor", _asyncGeneratorFunctionPrototype, PropertyFlags.Configurable);
        realm.DefineMethod(_asyncGeneratorPrototype, "next", 1, static (function, thisValue, arguments, _) => AsyncGeneratorOperations.Enqueue(function.Realm, thisValue, 0, arguments.Length > 0 ? arguments[0] : default));
        realm.DefineMethod(_asyncGeneratorPrototype, "return", 1, static (function, thisValue, arguments, _) => AsyncGeneratorOperations.Enqueue(function.Realm, thisValue, 2, arguments.Length > 0 ? arguments[0] : default));
        realm.DefineMethod(_asyncGeneratorPrototype, "throw", 1, static (function, thisValue, arguments, _) => AsyncGeneratorOperations.Enqueue(function.Realm, thisValue, 1, arguments.Length > 0 ? arguments[0] : default));
        _asyncGeneratorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "AsyncGenerator", PropertyFlags.Configurable);

        // %AsyncFromSyncIteratorPrototype%
        _asyncFromSyncIteratorPrototype = new JsObject(AsyncIteratorPrototype);
        AsyncFromSyncIterator.Initialize(this, _asyncFromSyncIteratorPrototype);

        // %eval%
        _eval = realm.CreateBuiltinFunction("eval", 1, static (function, _, arguments, _) => JsInterpreter.IndirectEval(function.Realm, arguments.Length > 0 ? arguments[0] : default));
        AddGlobal("eval", _eval);
    }

    private JsBuiltinFunction CreateDynamicFunctionConstructor(string name, DynamicFunctionKind kind, JsObject prototype)
    {
        var constructor = Realm.CreateBuiltinFunction(name, 1, (function, _, arguments, newTarget) => DynamicFunctions.CreateDynamicFunction(function, newTarget, kind, arguments), isConstructor: true, prototype: Function);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor, PropertyFlags.Configurable);
        return constructor;
    }

    private static JsGenerator GetGenerator(JsValue thisValue)
    {
        if (thisValue.Cell is JsGenerator generator)
            return generator;

        return ThrowHelper.ThrowTypeError<JsGenerator>("Generator method called on an incompatible receiver");
    }
}
