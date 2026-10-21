namespace Meziantou.Framework.JavaScriptEngine.Tests.Builtins;

/// <summary>An engine, a realm and helpers to call the built-ins from .NET (the interpreter does not exist yet).</summary>
internal sealed class BuiltinsTestContext : IDisposable
{
    private readonly JsEngine _engine;
    private readonly JsEngine.EngineScope _scope;

    public BuiltinsTestContext()
    {
        _engine = new JsEngine();
        Realm = _engine.CreateRealm();
        _scope = _engine.Enter();
    }

    public JsRealm Realm { get; }

    public JsObject Global(string name) => Realm.GlobalObject.Get(new PropertyKey(name)).AsObject();

    /// <summary>Calls a method of a global object, e.g. <c>Call("Array", "of", default, 1, 2)</c> for <c>Array.of(1, 2)</c>.</summary>
    public JsValue CallStatic(string global, string method, params JsValue[] arguments)
    {
        var target = Global(global);
        return target.Get(new PropertyKey(method)).AsObject().Call(target, arguments);
    }

    /// <summary>Calls a method of a prototype with a this value, e.g. <c>CallPrototype("String", "at", "abc", 1)</c>.</summary>
    public JsValue CallPrototype(string constructor, string method, JsValue thisValue, params JsValue[] arguments)
    {
        var prototype = Global(constructor).Get(new PropertyKey("prototype")).AsObject();
        return prototype.Get(new PropertyKey(method)).AsObject().Call(thisValue, arguments);
    }

    /// <summary>Invoke(value, name, arguments)</summary>
    public static JsValue Invoke(JsValue value, string name, params JsValue[] arguments) => JsOperations.Invoke(value, new PropertyKey(name), arguments);

    public static JsValue Get(JsValue value, string name) => JsOperations.GetV(value, new PropertyKey(name));

    public JsArray Array(params JsValue[] values) => JsArray.Create(Realm, values);

    public JsObject Object() => new(Realm.Intrinsics.ObjectPrototype);

    public JsFunction Function(Func<JsValue, JsValue[], JsValue> callback, int length = 0, string name = "")
    {
        return Realm.CreateBuiltinFunction(name, length, (_, thisValue, arguments, _) => callback(thisValue, arguments.ToArray()));
    }

    /// <summary>Asserts that the action throws a JavaScript error of the given constructor name (TypeError, RangeError...).</summary>
    public JsValue AssertThrows(string errorName, Action action)
    {
        var exception = Assert.Throws<JsException>(action);
        var error = exception.Value;
        var name = JsOperations.ToString(Get(error, "name"));
        Assert.Equal(errorName, name);
        return error;
    }

    /// <summary>Gets the elements of an array-like object.</summary>
    public static JsValue[] ToArray(JsValue value)
    {
        var obj = value.AsObject();
        var length = JsOperations.LengthOfArrayLike(obj);
        var result = new JsValue[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = obj.Get(new PropertyKey((uint)i));
        }

        return result;
    }

    /// <summary>Formats the elements of an array-like object, e.g. "1,a,undefined,&lt;hole&gt;".</summary>
    public static string Format(JsValue value)
    {
        var obj = value.AsObject();
        var length = JsOperations.LengthOfArrayLike(obj);
        var parts = new string[length];
        for (var i = 0; i < length; i++)
        {
            var key = new PropertyKey((uint)i);
            parts[i] = obj.HasProperty(key) ? obj.Get(key).ToString() : "<hole>";
        }

        return string.Join(',', parts);
    }

    /// <summary>Collects the values of an iterator.</summary>
    public static List<JsValue> Iterate(JsValue iterator)
    {
        var record = IteratorOperations.GetIteratorDirect(iterator.AsObject());
        return IteratorOperations.IteratorToList(record);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _engine.Dispose();
    }
}
