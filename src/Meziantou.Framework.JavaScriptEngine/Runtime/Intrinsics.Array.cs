namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Array%</summary>
    public JsFunction ArrayConstructor { get; private set; } = null!;

    /// <summary>%ArrayIteratorPrototype%</summary>
    public JsObject ArrayIteratorPrototype { get; private set; } = null!;

    /// <summary>%Array.prototype.values% (also Array.prototype[%Symbol.iterator%] and the %Symbol.iterator% of the arguments objects)</summary>
    public JsFunction ArrayPrototypeValues { get; private set; } = null!;

    /// <summary>%Array.prototype.toString% (also %TypedArray.prototype%.toString)</summary>
    public JsFunction ArrayPrototypeToString { get; private set; } = null!;

    partial void InitializeArray()
    {
        ArrayIteratorPrototype = new JsObject(IteratorPrototype);
        (ArrayConstructor, ArrayPrototypeValues, ArrayPrototypeToString) = ArrayBuiltins.Initialize(this);
    }
}
