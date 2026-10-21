namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The base class of the values that are not numbers nor <c>undefined</c>: objects, strings, symbols, BigInts, <c>null</c> and the booleans.</summary>
public abstract class JsCell
{
    private protected JsCell(JsValueKind kind)
    {
        Kind = kind;
    }

    internal JsValueKind Kind { get; }
}

/// <summary>The cell of the number values (the number itself is stored in the <see cref="JsValue"/>).</summary>
internal sealed class NumberCell : JsCell
{
    public static readonly NumberCell Instance = new();

    private NumberCell()
        : base(JsValueKind.Number)
    {
    }
}

internal sealed class NullCell : JsCell
{
    public static readonly NullCell Instance = new();

    private NullCell()
        : base(JsValueKind.Null)
    {
    }
}

internal sealed class BooleanCell : JsCell
{
    public static readonly BooleanCell True = new(value: true);
    public static readonly BooleanCell False = new(value: false);

    private BooleanCell(bool value)
        : base(JsValueKind.Boolean)
    {
        Value = value;
    }

    public bool Value { get; }
}

/// <summary>The marker of an uninitialized binding or of an array hole (its kind is <c>undefined</c>).</summary>
internal sealed class HoleCell : JsCell
{
    public static readonly HoleCell Instance = new();

    private HoleCell()
        : base(JsValueKind.Undefined)
    {
    }
}
