using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The generators, the async functions and the async generators (created on first use), and eval.</summary>
internal sealed partial class Intrinsics
{
    private JsObject? _asyncIteratorPrototype;
    private JsObject? _generatorFunctionPrototype;
    private JsObject? _generatorPrototype;
    private JsObject? _asyncFunctionPrototype;
    private JsObject? _asyncGeneratorFunctionPrototype;
    private JsObject? _asyncGeneratorPrototype;
    private JsObject? _asyncFromSyncIteratorPrototype;
    private JsFunction? _eval;
    private bool _isGeneratorsInitializing;

    /// <summary>%AsyncIteratorPrototype%</summary>
    public JsObject AsyncIteratorPrototype => EnsureGenerators()._asyncIteratorPrototype!;

    /// <summary>%GeneratorFunction.prototype%: the prototype of the generator functions.</summary>
    public JsObject GeneratorFunctionPrototype => EnsureGenerators()._generatorFunctionPrototype!;

    /// <summary>%GeneratorPrototype% (%GeneratorFunction.prototype.prototype%).</summary>
    public JsObject GeneratorPrototype => EnsureGenerators()._generatorPrototype!;

    /// <summary>%AsyncFunction.prototype%</summary>
    public JsObject AsyncFunctionPrototype => EnsureGenerators()._asyncFunctionPrototype!;

    /// <summary>%AsyncGeneratorFunction.prototype%</summary>
    public JsObject AsyncGeneratorFunctionPrototype => EnsureGenerators()._asyncGeneratorFunctionPrototype!;

    /// <summary>%AsyncGeneratorPrototype%</summary>
    public JsObject AsyncGeneratorPrototype => EnsureGenerators()._asyncGeneratorPrototype!;

    /// <summary>%AsyncFromSyncIteratorPrototype%</summary>
    public JsObject AsyncFromSyncIteratorPrototype => EnsureGenerators()._asyncFromSyncIteratorPrototype!;

    /// <summary>%eval%</summary>
    public JsFunction Eval => _eval!;

    private static readonly BuiltinMethodTable GeneratorPrototypeMethods =
    [
        new("next", 1, static (_, thisValue, arguments, _) => GetGenerator(thisValue).Resume(arguments.Length > 0 ? arguments[0] : default, kind: 0)),
        new("return", 1, static (_, thisValue, arguments, _) => GetGenerator(thisValue).Resume(arguments.Length > 0 ? arguments[0] : default, kind: 2)),
        new("throw", 1, static (_, thisValue, arguments, _) => GetGenerator(thisValue).Resume(arguments.Length > 0 ? arguments[0] : default, kind: 1)),
    ];

    private static readonly BuiltinMethodTable AsyncGeneratorPrototypeMethods =
    [
        new("next", 1, static (function, thisValue, arguments, _) => AsyncGeneratorOperations.Enqueue(function.Realm, thisValue, 0, arguments.Length > 0 ? arguments[0] : default)),
        new("return", 1, static (function, thisValue, arguments, _) => AsyncGeneratorOperations.Enqueue(function.Realm, thisValue, 2, arguments.Length > 0 ? arguments[0] : default)),
        new("throw", 1, static (function, thisValue, arguments, _) => AsyncGeneratorOperations.Enqueue(function.Realm, thisValue, 1, arguments.Length > 0 ? arguments[0] : default)),
    ];

    partial void InitializeGenerators()
    {
        // %eval%
        _eval = Realm.CreateBuiltinFunction("eval", 1, static (function, _, arguments, _) => JsInterpreter.IndirectEval(function.Realm, arguments.Length > 0 ? arguments[0] : default));
        AddGlobal("eval", _eval);
    }

    /// <summary>Creates the generator, async function and async generator built-ins, if they are not created yet (they have no global property).</summary>
    private Intrinsics EnsureGenerators()
    {
        if (_generatorFunctionPrototype is null && !_isGeneratorsInitializing)
        {
            _isGeneratorsInitializing = true;
            InitializeGeneratorBuiltins();
        }

        return this;
    }

    private void InitializeGeneratorBuiltins()
    {
        var realm = Realm;

        // %AsyncIteratorPrototype%
        _asyncIteratorPrototype = new JsObject(ObjectPrototype);
        realm.DefineMethod(_asyncIteratorPrototype, WellKnownSymbols.AsyncIterator, 0, static (_, thisValue, _, _) => thisValue);
        AsyncIteratorDisposeBuiltins.Initialize(realm, _asyncIteratorPrototype);

        // %GeneratorFunction%, %GeneratorFunction.prototype%, %GeneratorPrototype%
        _generatorFunctionPrototype = new JsObject(FunctionPrototype);
        _generatorPrototype = new JsObject(IteratorPrototype);
        var generatorFunction = CreateDynamicFunctionConstructor("GeneratorFunction", DynamicFunctionKind.Generator, _generatorFunctionPrototype);
        _generatorFunctionPrototype.DefineBuiltIn("prototype", _generatorPrototype, PropertyFlags.Configurable);
        _generatorFunctionPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "GeneratorFunction", PropertyFlags.Configurable);
        _generatorPrototype.DefineBuiltIn("constructor", _generatorFunctionPrototype, PropertyFlags.Configurable);
        realm.DefineMethods(_generatorPrototype, GeneratorPrototypeMethods);
        _generatorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Generator", PropertyFlags.Configurable);
        _ = generatorFunction;

        // %AsyncFunction%, %AsyncFunction.prototype%
        _asyncFunctionPrototype = new JsObject(FunctionPrototype);
        CreateDynamicFunctionConstructor("AsyncFunction", DynamicFunctionKind.Async, _asyncFunctionPrototype);
        _asyncFunctionPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "AsyncFunction", PropertyFlags.Configurable);

        // %AsyncGeneratorFunction%, %AsyncGeneratorFunction.prototype%, %AsyncGeneratorPrototype%
        _asyncGeneratorFunctionPrototype = new JsObject(FunctionPrototype);
        _asyncGeneratorPrototype = new JsObject(_asyncIteratorPrototype);
        CreateDynamicFunctionConstructor("AsyncGeneratorFunction", DynamicFunctionKind.AsyncGenerator, _asyncGeneratorFunctionPrototype);
        _asyncGeneratorFunctionPrototype.DefineBuiltIn("prototype", _asyncGeneratorPrototype, PropertyFlags.Configurable);
        _asyncGeneratorFunctionPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "AsyncGeneratorFunction", PropertyFlags.Configurable);
        _asyncGeneratorPrototype.DefineBuiltIn("constructor", _asyncGeneratorFunctionPrototype, PropertyFlags.Configurable);
        realm.DefineMethods(_asyncGeneratorPrototype, AsyncGeneratorPrototypeMethods);
        _asyncGeneratorPrototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "AsyncGenerator", PropertyFlags.Configurable);

        // %AsyncFromSyncIteratorPrototype%
        _asyncFromSyncIteratorPrototype = new JsObject(_asyncIteratorPrototype);
        AsyncFromSyncIterator.Initialize(this, _asyncFromSyncIteratorPrototype);
    }

    private JsBuiltinFunction CreateDynamicFunctionConstructor(string name, DynamicFunctionKind kind, JsObject prototype)
    {
        BuiltinFunctionCallback callback = kind switch
        {
            DynamicFunctionKind.Generator => static (function, _, arguments, newTarget) => DynamicFunctions.CreateDynamicFunction(function, newTarget, DynamicFunctionKind.Generator, arguments),
            DynamicFunctionKind.Async => static (function, _, arguments, newTarget) => DynamicFunctions.CreateDynamicFunction(function, newTarget, DynamicFunctionKind.Async, arguments),
            _ => static (function, _, arguments, newTarget) => DynamicFunctions.CreateDynamicFunction(function, newTarget, DynamicFunctionKind.AsyncGenerator, arguments),
        };

        var constructor = Realm.CreateBuiltinFunction(name, 1, callback, isConstructor: true, prototype: Function);
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
