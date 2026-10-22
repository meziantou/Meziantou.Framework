using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A JavaScript value: <c>undefined</c>, <c>null</c>, a boolean, a number, a string, a symbol, a BigInt or an object.
/// The default value is <c>undefined</c>. Numbers are not allocated; the other values reference a <see cref="JsCell"/>.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly struct JsValue : IEquatable<JsValue>
{
    // null: undefined; NumberCell.Instance: a number (in _number); the other cells are the value itself
    internal JsCell? Cell { get; }
    private readonly double _number;

    internal JsValue(JsCell? cell)
    {
        Cell = cell;
        _number = 0;
    }

    // The slot of a lazy property: the cell that computes the value, and an index (LazyCell)
    internal JsValue(JsCell cell, int index)
    {
        Cell = cell;
        _number = index;
    }

    /// <summary>Creates a number value.</summary>
    public JsValue(double value)
    {
        Cell = NumberCell.Instance;
        _number = value;
    }

    /// <summary>Gets the <c>undefined</c> value.</summary>
    public static JsValue Undefined => default;

    /// <summary>Gets the <c>null</c> value.</summary>
    public static JsValue Null { get; } = new(NullCell.Instance);

    /// <summary>Gets the <c>true</c> value.</summary>
    public static JsValue True { get; } = new(BooleanCell.True);

    /// <summary>Gets the <c>false</c> value.</summary>
    public static JsValue False { get; } = new(BooleanCell.False);

    /// <summary>The marker of an uninitialized binding (temporal dead zone) or of an array hole. It is never observable.</summary>
    internal static JsValue Hole { get; } = new(HoleCell.Instance);

    /// <summary>Gets the type of the value.</summary>
    public JsValueKind Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Cell is null ? JsValueKind.Undefined : Cell.Kind;
    }

    /// <summary>Gets whether the value is <c>undefined</c>.</summary>
    public bool IsUndefined => Cell is null;

    /// <summary>Gets whether the value is <c>null</c>.</summary>
    public bool IsNull => ReferenceEquals(Cell, NullCell.Instance);

    /// <summary>Gets whether the value is <c>undefined</c> or <c>null</c>.</summary>
    public bool IsNullOrUndefined => Cell is null || ReferenceEquals(Cell, NullCell.Instance);

    /// <summary>Gets whether the value is a boolean.</summary>
    public bool IsBoolean => Cell is BooleanCell;

    /// <summary>Gets whether the value is a number.</summary>
    public bool IsNumber
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ReferenceEquals(Cell, NumberCell.Instance);
    }

    /// <summary>Gets whether the value is a string.</summary>
    public bool IsString => Cell is JsString;

    /// <summary>Gets whether the value is a symbol.</summary>
    public bool IsSymbol => Cell is JsSymbol;

    /// <summary>Gets whether the value is a BigInt.</summary>
    public bool IsBigInt => Cell is JsBigInt;

    /// <summary>Gets whether the value is an object.</summary>
    public bool IsObject => Cell is JsObject;

    /// <summary>Gets whether the value is a function (an object with a [[Call]] internal method).</summary>
    public bool IsCallable => Cell is JsObject obj && obj.IsCallable;

    internal bool IsHole => ReferenceEquals(Cell, HoleCell.Instance);

    /// <summary>Gets the number of a number value.</summary>
    public double AsNumber()
    {
        Debug.Assert(IsNumber);
        return _number;
    }

    /// <summary>Gets the number of a value that is known to be a number (no check).</summary>
    internal double NumberValue => _number;

    /// <summary>
    /// Stores a value in a slot of the interpreter (a register, the stack). When the slot already has the same cell (the
    /// numbers: they share <see cref="NumberCell.Instance"/>), only the number is written: storing a reference into a heap
    /// object calls the write barrier of the GC, the main cost of the loops on numbers.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Store(ref JsValue slot, JsValue value)
    {
        if (ReferenceEquals(slot.Cell, value.Cell))
        {
            ref var layout = ref unsafe(Unsafe.As<JsValue, ValueLayout>(ref slot));
            layout.Number = value._number;
        }
        else
        {
            slot = value;
        }
    }

    /// <summary>Stores a number in a slot of the interpreter (see <see cref="Store"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void StoreNumber(ref JsValue slot, double value)
    {
        if (ReferenceEquals(slot.Cell, NumberCell.Instance))
        {
            ref var layout = ref unsafe(Unsafe.As<JsValue, ValueLayout>(ref slot));
            layout.Number = value;
        }
        else
        {
            slot = new JsValue(value);
        }
    }

    /// <summary>The fields of <see cref="JsValue"/> (same types, same order) with a writable number.</summary>
    private struct ValueLayout
    {
#pragma warning disable CS0649 // Only the number is written
        public JsCell? Cell;
#pragma warning restore CS0649
        public double Number;
    }

    /// <summary>Gets the boolean of a boolean value.</summary>
    public bool AsBoolean() => ((BooleanCell)Cell!).Value;

    /// <summary>Gets the string of a string value.</summary>
    public string AsString() => ((JsString)Cell!).ToString();

    internal JsString AsJsString() => (JsString)Cell!;

    /// <summary>Gets the symbol of a symbol value.</summary>
    public JsSymbol AsSymbol() => (JsSymbol)Cell!;

    /// <summary>Gets the integer of a BigInt value.</summary>
    public BigInteger AsBigInt() => ((JsBigInt)Cell!).Value;

    internal JsBigInt AsJsBigInt() => (JsBigInt)Cell!;

    /// <summary>Gets the object of an object value.</summary>
    public JsObject AsObject() => (JsObject)Cell!;

    /// <summary>Gets the object of the value, or null when the value is not an object.</summary>
    public JsObject? TryGetObject() => Cell as JsObject;

    /// <summary>Creates a boolean value.</summary>
    public static JsValue FromBoolean(bool value) => value ? True : False;

    /// <summary>Creates a number value.</summary>
    public static JsValue FromNumber(double value) => new(value);

    /// <summary>Creates a string value.</summary>
    public static JsValue FromString(string value) => new(JsString.Create(value));

    /// <summary>Creates a BigInt value.</summary>
    public static JsValue FromBigInt(BigInteger value) => new(new JsBigInt(value));

    /// <summary>Creates an object value, or <c>null</c> when the object is null.</summary>
    public static JsValue FromObject(JsObject? value) => value is null ? Null : new(value);

    /// <summary>Converts a number to a value.</summary>
    public static implicit operator JsValue(double value) => new(value);

    /// <summary>Converts a number to a value.</summary>
    public static implicit operator JsValue(int value) => new(value);

    /// <summary>Converts a boolean to a value.</summary>
    public static implicit operator JsValue(bool value) => FromBoolean(value);

    /// <summary>Converts a string to a value.</summary>
    public static implicit operator JsValue(string value) => FromString(value);

    /// <summary>Converts an object to a value (<c>null</c> when the object is null).</summary>
    public static implicit operator JsValue(JsObject? value) => FromObject(value);

    /// <summary>Converts a symbol to a value.</summary>
    public static implicit operator JsValue(JsSymbol value) => new(value);

    internal static JsValue FromJsString(JsString value) => new(value);

    /// <summary>Compares two values with the SameValue algorithm (https://tc39.es/ecma262/#sec-samevalue).</summary>
    public bool Equals(JsValue other) => JsOperations.SameValue(this, other);

    /// <summary>Compares the value with another <see cref="JsValue"/> with the SameValue algorithm.</summary>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsValue value && Equals(value);

    /// <summary>Gets a hash code consistent with SameValue.</summary>
    public override int GetHashCode()
    {
        return Kind switch
        {
            JsValueKind.Undefined => 0,
            JsValueKind.Number => _number.GetHashCode(),
            JsValueKind.String => AsJsString().GetHashCode(),
            JsValueKind.BigInt => AsBigInt().GetHashCode(),
            _ => RuntimeHelpers.GetHashCode(Cell!),
        };
    }

    /// <summary>Compares two values with the SameValue algorithm.</summary>
    public static bool operator ==(JsValue left, JsValue right) => left.Equals(right);

    /// <summary>Compares two values with the SameValue algorithm.</summary>
    public static bool operator !=(JsValue left, JsValue right) => !left.Equals(right);

    /// <summary>Returns a representation of the value for debugging; it never runs JavaScript code (use <c>ToString</c> of <see cref="JsOperations"/> for the ToString operation).</summary>
    public override string ToString()
    {
        return Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "null",
            JsValueKind.Boolean => AsBoolean() ? "true" : "false",
            JsValueKind.Number => NumberFormatting.ToString(_number),
            JsValueKind.String => AsString(),
            JsValueKind.Symbol => AsSymbol().ToString(),
            JsValueKind.BigInt => AsBigInt().ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => Cell is HoleCell ? "<hole>" : "[object " + AsObject().ClassName + "]",
        };
    }

    private string DebuggerDisplay => Kind == JsValueKind.String ? "\"" + AsString() + "\"" : ToString();
}
