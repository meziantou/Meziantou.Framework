namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

/// <summary>An engine, a realm and the engine scope, with helpers to call the built-ins from C# (the interpreter is not available yet).</summary>
internal sealed class CollectionsTestEngine : IDisposable
{
    private readonly JsEngine.EngineScope _scope;

    public CollectionsTestEngine(JsEngineOptions? options = null)
    {
        Engine = new JsEngine(options);
        Realm = Engine.CreateRealm();
        _scope = Engine.Enter();
    }

    public JsEngine Engine { get; }

    public JsRealm Realm { get; }

    public List<string> Log { get; } = [];

    public JsObject Global(string name) => Realm.GlobalObject.Get(new PropertyKey(name)).AsObject();

    public static JsValue Get(JsValue target, string name) => JsOperations.GetV(target, new PropertyKey(name));

    public static JsValue Get(JsValue target, JsSymbol symbol) => JsOperations.GetV(target, new PropertyKey(symbol));

    /// <summary>Calls the method <paramref name="name"/> of <paramref name="target"/>.</summary>
    public static JsValue Invoke(JsValue target, string name, params JsValue[] arguments) => JsOperations.Invoke(target, new PropertyKey(name), arguments);

    /// <summary>Constructs the global constructor <paramref name="name"/>.</summary>
    public JsValue New(string name, params JsValue[] arguments) => JsOperations.Construct(Global(name), arguments);

    /// <summary>Creates a function implemented by a .NET delegate.</summary>
    public JsBuiltinFunction Function(Func<JsValue, JsValue[], JsValue> callback, int length = 0)
    {
        return Realm.CreateBuiltinFunction("f", length, (_, thisValue, arguments, _) => callback(thisValue, arguments.ToArray()));
    }

    /// <summary>Creates a function that appends a message to <see cref="Log"/> and returns undefined.</summary>
    public JsBuiltinFunction Logger(string prefix)
    {
        return Function((_, arguments) =>
        {
            Log.Add(arguments.Length > 0 ? prefix + ":" + arguments[0].ToString() : prefix);
            return JsValue.Undefined;
        });
    }

    /// <summary>Creates an array of the values.</summary>
    public JsArray Array(params JsValue[] values) => JsArray.Create(Realm, values);

    /// <summary>Creates an iterable object that produces the values (it does not depend on the Array built-ins).</summary>
    public JsObject Iterable(params JsValue[] values)
    {
        var iterable = new JsObject(Realm.Intrinsics.ObjectPrototype);
        iterable.DefineBuiltIn(new PropertyKey(WellKnownSymbols.Iterator), Function((_, _) =>
        {
            var index = 0;
            var iterator = new JsObject(Realm.Intrinsics.ObjectPrototype);
            iterator.DefineBuiltIn(new PropertyKey("next"), Function((_, _) =>
            {
                if (index < values.Length)
                    return IteratorOperations.CreateIteratorResultObject(Realm, values[index++], done: false);

                return IteratorOperations.CreateIteratorResultObject(Realm, JsValue.Undefined, done: true);
            }));
            iterator.DefineBuiltIn(new PropertyKey("return"), Function((_, _) =>
            {
                Log.Add("return");
                return new JsObject(Realm.Intrinsics.ObjectPrototype);
            }));
            return iterator;
        }));
        return iterable;
    }

    /// <summary>Creates an ordinary object with the data properties.</summary>
    public JsObject Object(params (string Name, JsValue Value)[] properties)
    {
        var result = new JsObject(Realm.Intrinsics.ObjectPrototype);
        foreach (var (name, value) in properties)
        {
            result.CreateDataPropertyOrThrow(new PropertyKey(name), value);
        }

        return result;
    }

    /// <summary>Gets the elements of an array-like object.</summary>
    public static List<JsValue> ToList(JsValue arrayLike)
    {
        var obj = arrayLike.AsObject();
        var length = JsOperations.LengthOfArrayLike(obj);
        var result = new List<JsValue>();
        for (var i = 0; i < length; i++)
        {
            result.Add(obj.Get(new PropertyKey((uint)i)));
        }

        return result;
    }

    /// <summary>Gets the values produced by an iterable, or by an iterator (%Iterator.prototype%[Symbol.iterator] may not exist).</summary>
    public static List<JsValue> Iterate(JsValue iterable)
    {
        var method = JsOperations.GetMethod(iterable, new PropertyKey(WellKnownSymbols.Iterator));
        var record = method is null ? IteratorOperations.GetIteratorDirect(iterable.AsObject()) : IteratorOperations.GetIteratorFromMethod(iterable, method);
        return IteratorOperations.IteratorToList(record);
    }

    /// <summary>Asserts that the value is a native error of the type, created in the realm.</summary>
    public void AssertError(ErrorType type, JsValue value)
    {
        Assert.True(value.IsObject, "The value is not an object: " + value);
        Assert.Same(Realm.Intrinsics.GetErrorPrototype(type), value.AsObject().PrototypeSlot);
    }

    /// <summary>Asserts that the action throws a native error of the type.</summary>
    public void AssertThrows(ErrorType type, Action action)
    {
        var exception = Assert.Throws<JsException>(action);
        AssertError(type, exception.Value);
    }

    public void Dispose()
    {
        _scope.Dispose();
        Engine.Dispose();
    }
}
