namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The value of a data property computed the first time the property is read (the slot of a property with
/// <see cref="PropertyFlags.Lazy"/>): e.g. a built-in object of a realm, or a built-in method.
/// </summary>
internal abstract class LazyCell() : JsCell(JsValueKind.Undefined)
{
    /// <summary>Computes the value of the property.</summary>
    public abstract JsValue Evaluate();
}

/// <summary>A lazy value computed by a factory, which gets a state (a host does not allocate a closure per property).</summary>
internal sealed class LazyValue(Func<object?, JsValue> factory, object? state) : LazyCell
{
    public LazyValue(Func<JsValue> factory)
        : this(Invoke, factory)
    {
    }

    /// <summary>Gets the callback that invokes a <see cref="Func{JsValue}"/> given as the state.</summary>
    public static Func<object?, JsValue> Invoke { get; } = static state => ((Func<JsValue>)state!)();

    public override JsValue Evaluate() => factory(state);
}

/// <summary>
/// A built-in method (<see cref="JsRealm.DefineMethod"/>) whose function object is created on first access: a realm defines
/// hundreds of methods, and most scripts use a few of them. The key is stored without the padding of <see cref="PropertyKey"/>.
/// </summary>
internal sealed class LazyBuiltinMethod(JsRealm realm, in PropertyKey name, int length, BuiltinFunctionCallback callback) : LazyCell
{
    private readonly object? _nameValue = name.RawValue;
    private readonly uint _nameIndex = name.RawIndex;

    public override JsValue Evaluate() => realm.CreateBuiltinFunction(PropertyKey.FromRaw(_nameValue, _nameIndex), length, callback);
}
