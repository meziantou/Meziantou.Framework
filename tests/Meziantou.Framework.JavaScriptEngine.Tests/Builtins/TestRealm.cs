namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

/// <summary>An engine and a realm entered on the current thread, to call the built-ins directly.</summary>
internal sealed class TestRealm : IDisposable
{
    private readonly JsEngine.EngineScope _scope;

    public TestRealm()
    {
        Engine = new JsEngine();
        Realm = Engine.CreateRealm();
        _scope = Engine.Enter();
        Engine.CurrentRealm = Realm;
    }

    public JsEngine Engine { get; }

    public JsRealm Realm { get; }

    /// <summary>Gets a value from a dotted path of properties of the global object (e.g. "Number.prototype.toFixed").</summary>
    public JsValue Get(string path)
    {
        JsValue value = Realm.GlobalObject;
        foreach (var part in path.Split('.'))
        {
            value = JsOperations.GetV(value, new PropertyKey(part));
        }

        return value;
    }

    /// <summary>Calls the function at a path of the global object with the object that holds it as <c>this</c>.</summary>
    public JsValue Call(string path, params JsValue[] arguments)
    {
        var index = path.LastIndexOf('.', StringComparison.Ordinal);
        var thisValue = index < 0 ? JsValue.Undefined : Get(path[..index]);
        return JsOperations.Call(Get(path), thisValue, arguments);
    }

    /// <summary>Calls the function at a path of the global object with a <c>this</c> value.</summary>
    public JsValue CallWithThis(JsValue thisValue, string path, params JsValue[] arguments) => JsOperations.Call(Get(path), thisValue, arguments);

    /// <summary>Constructs the constructor at a path of the global object.</summary>
    public JsValue Construct(string path, params JsValue[] arguments) => JsOperations.Construct(Get(path).AsObject(), arguments);

    /// <summary>Calls a function and returns the name of the error it throws.</summary>
    public string? GetErrorName(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (JsException ex)
        {
            return JsOperations.ToString(ex.Value.AsObject().Get(new PropertyKey("name")));
        }
    }

    /// <summary>Calls a function and returns the error it throws.</summary>
    public JsObject GetError(Action action)
    {
        try
        {
            action();
        }
        catch (JsException ex)
        {
            return ex.Value.AsObject();
        }

        throw new InvalidOperationException("No exception was thrown");
    }

    /// <summary>Creates a function of the realm from a .NET callback.</summary>
    public JsValue CreateFunction(Func<JsValue, JsValue[], JsValue> callback, int length = 0)
    {
        return Realm.CreateBuiltinFunction("f", length, (_, thisValue, arguments, _) => callback(thisValue, arguments.ToArray()));
    }

    /// <summary>Creates an ordinary object with the properties.</summary>
    public JsObject CreateObject(params (string Name, JsValue Value)[] properties)
    {
        var obj = new JsObject(Realm.Intrinsics.ObjectPrototype);
        foreach (var (name, value) in properties)
        {
            obj.CreateDataPropertyOrThrow(new PropertyKey(name), value);
        }

        return obj;
    }

    /// <summary>Creates an array of the realm.</summary>
    public JsArray CreateArray(params JsValue[] values) => JsArray.Create(Realm, values);

    /// <summary>Creates an iterable object whose iterator returns the values (it does not depend on %ArrayIteratorPrototype%).</summary>
    public JsObject CreateIterable(params JsValue[] values)
    {
        var iterable = CreateObject();
        var iteratorMethod = CreateFunction((_, _) =>
        {
            var index = 0;
            var iterator = CreateObject();
            iterator.CreateDataPropertyOrThrow(new PropertyKey("next"), CreateFunction((_, _) =>
            {
                var done = index >= values.Length;
                var value = done ? JsValue.Undefined : values[index++];
                return CreateObject(("value", value), ("done", done));
            }));
            iterator.CreateDataPropertyOrThrow(new PropertyKey("return"), CreateFunction((_, _) =>
            {
                ReturnCount++;
                return CreateObject();
            }));
            return iterator;
        });
        iterable.CreateDataPropertyOrThrow(new PropertyKey(WellKnownSymbols.Iterator), iteratorMethod);
        return iterable;
    }

    /// <summary>Gets the number of calls of the return method of the iterators created by <see cref="CreateIterable"/>.</summary>
    public int ReturnCount { get; private set; }

    public void Dispose()
    {
        _scope.Dispose();
        Engine.Dispose();
    }
}
