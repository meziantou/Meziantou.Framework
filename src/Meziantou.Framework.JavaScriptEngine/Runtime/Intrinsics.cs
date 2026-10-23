namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The intrinsic objects of a realm (https://tc39.es/ecma262/#sec-well-known-intrinsic-objects). The prototype objects
/// are created first, so that the built-ins can reference each other; each built-in then adds its properties
/// (Builtins/*.cs) and its global binding.
/// </summary>
internal sealed partial class Intrinsics
{
    // The number of global properties that the built-ins define (learned by the first realm of the process: the next ones
    // allocate the property table of their global object once)
    private static int s_globalCount = 96;

    private JsObject? _globalObject;
    private LazyGlobals? _lazyGlobals;
    private readonly JsObject[] _errorPrototypes = new JsObject[ErrorBuiltins.Types.Length];
    private readonly JsObject[] _errorConstructors = new JsObject[ErrorBuiltins.Types.Length];
    private Interpreter.LegacyFunctionProperty? _functionCallerProperty;
    private Interpreter.LegacyFunctionProperty? _functionArgumentsProperty;

    public Intrinsics(JsRealm realm)
    {
        Realm = realm;

        // %Object.prototype% is an immutable prototype exotic object
        ObjectPrototype = new JsObject(prototype: null);
        ObjectPrototype.Flags |= ObjectFlags.ImmutablePrototype;

        // %Function.prototype% is a built-in function that returns undefined
        FunctionPrototype = new JsBuiltinFunction(realm, static (_, _, _, _) => JsValue.Undefined, isConstructor: false, ObjectPrototype);

        foreach (var type in ErrorBuiltins.Types)
        {
            _errorPrototypes[(int)type] = new JsObject(type == ErrorType.Error ? ObjectPrototype : null);
        }

        BooleanPrototype = new JsPrimitiveObject(ObjectPrototype, ObjectClass.Boolean, JsValue.False);
        NumberPrototype = new JsPrimitiveObject(ObjectPrototype, ObjectClass.Number, 0);
        StringPrototype = new JsStringObject(ObjectPrototype, JsString.Empty);
        SymbolPrototype = new JsObject(ObjectPrototype);
        BigIntPrototype = new JsObject(ObjectPrototype);
        ArrayPrototype = new JsArray(ObjectPrototype);
        IteratorPrototype = new JsObject(ObjectPrototype);
    }

    public JsRealm Realm { get; }

    public JsObject ObjectPrototype { get; }

    public JsFunction FunctionPrototype { get; }

    public JsObject BooleanPrototype { get; }

    public JsObject NumberPrototype { get; }

    public JsObject StringPrototype { get; }

    public JsObject SymbolPrototype { get; }

    public JsObject BigIntPrototype { get; }

    public JsArray ArrayPrototype { get; }

    /// <summary>%Iterator.prototype%</summary>
    public JsObject IteratorPrototype { get; }

    /// <summary>%Object%</summary>
    public JsFunction Object { get; private set; } = null!;

    /// <summary>%Function%</summary>
    public JsFunction Function { get; private set; } = null!;

    /// <summary>%ThrowTypeError%</summary>
    public JsFunction ThrowTypeError { get; private set; } = null!;

    /// <summary>The value of the caller own property of the sloppy functions.</summary>
    public Interpreter.LegacyFunctionProperty FunctionCallerProperty => _functionCallerProperty ??= new(Realm, isCaller: true);

    /// <summary>The value of the arguments own property of the sloppy functions.</summary>
    public Interpreter.LegacyFunctionProperty FunctionArgumentsProperty => _functionArgumentsProperty ??= new(Realm, isCaller: false);

    public JsObject GetErrorPrototype(ErrorType type) => _errorPrototypes[(int)type];

    public JsObject GetErrorConstructor(ErrorType type) => _errorConstructors[(int)type];

    /// <summary>Creates the properties of the intrinsics, and the properties of the global object (SetDefaultGlobalBindings).</summary>
    public void Initialize(JsObject globalObject)
    {
        // The global object has many (lazy) properties, and a host usually adds more: a table, not a chain of shapes
        globalObject.UsePropertyTable(s_globalCount);
        _globalObject = globalObject;
        ThrowTypeError = CreateThrowTypeError();
        ErrorBuiltins.Initialize(this, _errorPrototypes, _errorConstructors);
        InitializeBuiltins();
        _globalObject = null;
        s_globalCount = Math.Max(s_globalCount, globalObject.GetOwnPropertyCount());
    }

    /// <summary>Initializes the other built-ins, in this order. Each one is implemented in its own file (Intrinsics.&lt;Area&gt;.cs).</summary>
    private void InitializeBuiltins()
    {
        InitializeObject();
        InitializeFunction();
        InitializeBoolean();
        InitializeSymbol();
        InitializeNumber();
        InitializeBigInt();
        InitializeMath();
        InitializeGlobalFunctions();
        InitializeString();
        InitializeIterator();
        InitializeArray();
        InitializeJson();
        InitializeReflect();
        InitializeProxy();
        InitializeCollections();
        InitializeWeakRefs();
        InitializeShadowRealm();
        InitializePromise();
        InitializeGenerators();
        InitializeAsyncFunctions();
        InitializeRegExp();
        InitializeBinaryData();
        InitializeDate();
        InitializeDisposable();
        InitializeTemporal();
        InitializeIntl();
    }

    partial void InitializeObject();

    partial void InitializeFunction();

    partial void InitializeBoolean();

    partial void InitializeSymbol();

    partial void InitializeNumber();

    partial void InitializeBigInt();

    partial void InitializeMath();

    partial void InitializeGlobalFunctions();

    partial void InitializeString();

    partial void InitializeIterator();

    partial void InitializeArray();

    partial void InitializeJson();

    partial void InitializeReflect();

    partial void InitializeProxy();

    partial void InitializeCollections();

    partial void InitializeWeakRefs();

    partial void InitializeShadowRealm();

    partial void InitializePromise();

    partial void InitializeGenerators();

    partial void InitializeAsyncFunctions();

    partial void InitializeRegExp();

    partial void InitializeBinaryData();

    partial void InitializeDate();

    partial void InitializeDisposable();

    partial void InitializeTemporal();

    partial void InitializeIntl();

    /// <summary>
    /// Adds a property of the global object (SetDefaultGlobalBindings). It is ignored once the intrinsics are initialized:
    /// a built-in created on first use has a lazy global property (<see cref="AddLazyGlobal"/>).
    /// </summary>
    public void AddGlobal(PropertyKey name, JsValue value, PropertyFlags flags = PropertyFlags.BuiltIn)
    {
        _globalObject?.DefineBuiltIn(name, value, flags);
    }

    /// <summary>Adds a property of the global object whose value is created on first use (a built-in that is rarely used, e.g. %Temporal%).</summary>
    public void AddLazyGlobal(PropertyKey name, Func<Intrinsics, JsValue> factory, PropertyFlags flags = PropertyFlags.BuiltIn)
    {
        if (_globalObject is not { } globalObject)
            return;

        var lazyGlobals = _lazyGlobals ??= new LazyGlobals(this);
        globalObject.DefineLazy(name, lazyGlobals, lazyGlobals.Add(factory), flags);
    }

    /// <summary>The global properties created on first use: the slot of a property has the index of its factory.</summary>
    private sealed class LazyGlobals(Intrinsics intrinsics) : LazyCell
    {
        // The number of lazy global properties (learned by the first realm of the process)
        private static int s_count = 32;

        private Func<Intrinsics, JsValue>[] _factories = new Func<Intrinsics, JsValue>[s_count];
        private int _count;

        public int Add(Func<Intrinsics, JsValue> factory)
        {
            if (_count == _factories.Length)
            {
                Array.Resize(ref _factories, _count * 2);
            }

            _factories[_count] = factory;
            s_count = Math.Max(s_count, _count + 1);
            return _count++;
        }

        public override JsValue Evaluate(int index) => _factories[index](intrinsics);
    }

    /// <summary>Sets %Object%; called by the built-in that creates it.</summary>
    public void SetObjectConstructor(JsFunction constructor) => Object = constructor;

    /// <summary>Sets %Function%; called by the built-in that creates it.</summary>
    public void SetFunctionConstructor(JsFunction constructor) => Function = constructor;

    /// <summary>Creates the wrapper object of a primitive value (ToObject).</summary>
    public JsObject CreatePrimitiveWrapper(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Boolean => new JsPrimitiveObject(BooleanPrototype, ObjectClass.Boolean, value),
            JsValueKind.Number => new JsPrimitiveObject(NumberPrototype, ObjectClass.Number, value),
            JsValueKind.String => new JsStringObject(StringPrototype, value.AsJsString()),
            JsValueKind.Symbol => new JsPrimitiveObject(SymbolPrototype, ObjectClass.Symbol, value),
            JsValueKind.BigInt => new JsPrimitiveObject(BigIntPrototype, ObjectClass.BigInt, value),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Not a primitive value"),
        };
    }

    /// <summary>%ThrowTypeError% (https://tc39.es/ecma262/#sec-%throwtypeerror%): non-extensible, length and name not configurable.</summary>
    private JsBuiltinFunction CreateThrowTypeError()
    {
        var function = new JsBuiltinFunction(Realm, static (_, _, _, _) => ThrowHelper.ThrowTypeError<JsValue>("'caller', 'callee', and 'arguments' properties may not be accessed on strict mode functions or the arguments objects for calls to them"), isConstructor: false, FunctionPrototype);
        function.DefineBuiltIn("length", 0, PropertyFlags.None);
        function.DefineBuiltIn("name", "", PropertyFlags.None);
        function.PreventExtensions();
        return function;
    }
}
