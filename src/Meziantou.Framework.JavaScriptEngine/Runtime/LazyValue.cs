using System.Collections;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The value of a data property computed the first time the property is read (the slot of a property with
/// <see cref="PropertyFlags.Lazy"/>): e.g. a built-in object of a realm, or a built-in method. The slot has the cell and
/// an index (a cell can compute the values of several properties: <see cref="LazyBuiltinMethodTable"/>).
/// </summary>
internal abstract class LazyCell() : JsCell(JsValueKind.Undefined)
{
    /// <summary>Computes the value of the property whose slot has the index.</summary>
    public abstract JsValue Evaluate(int index);
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

    public override JsValue Evaluate(int index) => factory(state);
}

/// <summary>
/// A built-in method (<see cref="JsRealm.DefineMethod"/>) whose function object is created on first access: a realm defines
/// hundreds of methods, and most scripts use a few of them. The key is stored without the padding of <see cref="PropertyKey"/>.
/// </summary>
internal sealed class LazyBuiltinMethod(JsRealm realm, in PropertyKey name, int length, BuiltinFunctionCallback callback) : LazyCell
{
    private readonly object? _nameValue = name.RawValue;
    private readonly uint _nameIndex = name.RawIndex;

    public override JsValue Evaluate(int index) => realm.CreateBuiltinFunction(PropertyKey.FromRaw(_nameValue, _nameIndex), length, callback);
}

/// <summary>A built-in method of a <see cref="BuiltinMethodTable"/>.</summary>
internal readonly struct BuiltinMethod(PropertyKey name, int length, BuiltinFunctionCallback callback, PropertyFlags flags = PropertyFlags.BuiltIn)
{
    public PropertyKey Name { get; } = name;

    public int Length { get; } = length;

    public BuiltinFunctionCallback Callback { get; } = callback;

    public PropertyFlags Flags { get; } = flags;
}

/// <summary>
/// The built-in methods that a built-in defines on an object (<see cref="JsRealm.DefineMethods"/>): the tables are static,
/// created once per process and shared by the realms, so a realm does not allocate an object per method.
/// </summary>
/// <remarks>
/// A table also learns the number of properties of the objects that get its methods (<see cref="PropertyCount"/>): the
/// next realms allocate the property tables of these objects once, with their final size.
/// </remarks>
[CollectionBuilder(typeof(BuiltinMethodTable), nameof(Create))]
internal sealed class BuiltinMethodTable : IEnumerable<BuiltinMethod>
{
    // The learned count is bounded: a script can add many properties to a built-in object of its realm
    private const int MaxPropertyCount = 256;

    private BuiltinMethodTable(BuiltinMethod[] methods)
    {
        Methods = methods;
    }

    public BuiltinMethod[] Methods { get; }

    /// <summary>Gets the largest number of properties of an object that got the methods (the engines of several threads can update it: it is a hint).</summary>
    public int PropertyCount { get; private set; }

    public static BuiltinMethodTable Create(ReadOnlySpan<BuiltinMethod> methods) => new(methods.ToArray());

    /// <summary>Remembers the number of properties of an object that got the methods.</summary>
    public void LearnPropertyCount(int count)
    {
        if (count > PropertyCount && count <= MaxPropertyCount)
        {
            PropertyCount = count;
        }
    }

    public IEnumerator<BuiltinMethod> GetEnumerator() => ((IEnumerable<BuiltinMethod>)Methods).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>The methods of a <see cref="BuiltinMethodTable"/> defined on an object of a realm: the slot of a method has the cell and the index of the method in the table.</summary>
internal sealed class LazyBuiltinMethodTable(JsRealm realm, BuiltinMethod[] methods) : LazyCell
{
    public override JsValue Evaluate(int index)
    {
        ref readonly var method = ref methods[index];
        return realm.CreateBuiltinFunction(method.Name, method.Length, method.Callback);
    }
}
