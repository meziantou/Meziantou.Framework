namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

/// <summary>An engine, a realm and helpers to call the built-ins from .NET (the interpreter is not available yet).</summary>
internal sealed class FundamentalsTestContext : IDisposable
{
    private readonly JsEngine.EngineScope _scope;

    public FundamentalsTestContext()
    {
        Engine = new JsEngine();
        Realm = Engine.CreateRealm();
        _scope = Engine.Enter();
        Engine.CurrentRealm = Realm;
    }

    public JsEngine Engine { get; }

    public JsRealm Realm { get; }

    public Intrinsics Intrinsics => Realm.Intrinsics;

    public JsObject Global(string name) => Realm.GlobalObject.Get(new PropertyKey(name)).AsObject();

    public static JsValue Get(JsValue obj, string name) => JsOperations.GetV(obj, new PropertyKey(name));

    public static JsValue Get(JsValue obj, PropertyKey key) => JsOperations.GetV(obj, key);

    /// <summary>Creates an iterable object whose iterator returns the values (arrays are not iterable until Array is implemented).</summary>
    public JsObject NewIterable(params JsValue[] values) => NewIterable(log: null, values);

    /// <summary>Creates an iterable object whose iterator returns the values, and logs "return" when the iterator is closed.</summary>
    public JsObject NewIterable(List<string>? log, params JsValue[] values)
    {
        var iterable = NewObject();
        iterable.CreateDataPropertyOrThrow(WellKnownSymbols.Iterator, Function((_, _) =>
        {
            var index = 0;
            var iterator = NewObject();
            iterator.CreateDataPropertyOrThrow("next", Function((_, _) =>
            {
                var done = index >= values.Length;
                var value = done ? JsValue.Undefined : values[index++];
                return IteratorOperations.CreateIteratorResultObject(Realm, value, done);
            }));
            iterator.CreateDataPropertyOrThrow("return", Function((_, _) =>
            {
                log?.Add("return");
                return NewObject();
            }));
            return iterator;
        }));
        return iterable;
    }

    /// <summary>Calls <c>obj[name](...arguments)</c>.</summary>
    public static JsValue Invoke(JsValue obj, string name, params JsValue[] arguments) => JsOperations.Invoke(obj, new PropertyKey(name), arguments);

    /// <summary>Calls <c>global[name](...arguments)</c> where global is a global binding (e.g. <c>Object.keys</c>).</summary>
    public JsValue CallStatic(string global, string name, params JsValue[] arguments) => Invoke(Global(global), name, arguments);

    /// <summary>Calls the method of a prototype with a <c>this</c> value (e.g. <c>Object.prototype.toString.call(value)</c>).</summary>
    public JsValue CallPrototypeMethod(string global, string name, JsValue thisValue, params JsValue[] arguments)
    {
        var prototype = Get(Global(global), "prototype");
        var method = Get(prototype, name);
        return JsOperations.Call(method, thisValue, arguments);
    }

    public JsObject NewObject() => new(Intrinsics.ObjectPrototype);

    public JsArray NewArray(params JsValue[] values) => JsArray.Create(Realm, values);

    public JsBuiltinFunction Function(Func<JsValue, JsValue[], JsValue> callback, string name = "f", int length = 0, bool isConstructor = false)
    {
        return Realm.CreateBuiltinFunction(name, length, (_, thisValue, arguments, _) => callback(thisValue, arguments.ToArray()), isConstructor);
    }

    public JsBuiltinFunction Function(Func<JsValue, JsValue[], JsObject?, JsValue> callback, string name = "f", int length = 0, bool isConstructor = false)
    {
        return Realm.CreateBuiltinFunction(name, length, (_, thisValue, arguments, newTarget) => callback(thisValue, arguments.ToArray(), newTarget), isConstructor);
    }

    /// <summary>Creates a proxy with the traps (a name and a callback receiving the arguments of the trap).</summary>
    public JsValue NewProxy(JsValue target, params (string Name, Func<JsValue[], JsValue> Trap)[] traps)
    {
        var handler = NewObject();
        foreach (var (name, trap) in traps)
        {
            handler.CreateDataPropertyOrThrow(name, Function((_, args) => trap(args), name));
        }

        return JsOperations.Construct(Global("Proxy"), [target, handler]);
    }

    /// <summary>Asserts that the action throws a JavaScript error of the type and returns it.</summary>
    public JsObject AssertThrows(ErrorType type, Action action)
    {
        var exception = Assert.Throws<JsException>(action);
        var error = exception.Value.AsObject();
        Assert.Same(Intrinsics.GetErrorPrototype(type), error.GetPrototypeOf());
        return error;
    }

    public void Dispose()
    {
        _scope.Dispose();
        Engine.Dispose();
    }
}
