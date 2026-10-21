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

    /// <summary>%ArrayIteratorPrototype%.next</summary>
    public JsValue ArrayIteratorNext { get; private set; }

    // The state of IsArrayIterationIntact, valid while the epoch of the inline caches does not change: the positions of
    // the properties whose values are compared (a value can change without a change of structure)
    private int _arrayIterationEpoch = -1;
    private bool _arrayIterationShapeIntact;
    private int _arrayIteratorMethodPosition;
    private bool _arrayIteratorMethodInTable;
    private int _arrayIteratorNextPosition;
    private bool _arrayIteratorNextInTable;

    partial void InitializeArray()
    {
        ArrayIteratorPrototype = new JsObject(IteratorPrototype);
        (ArrayConstructor, ArrayPrototypeValues, ArrayPrototypeToString) = ArrayBuiltins.Initialize(this);
        ArrayIteratorNext = ArrayIteratorPrototype.Get(new PropertyKey("next"));
    }

    /// <summary>
    /// Gets whether the iteration of the arrays of the realm is not observable: %Array.prototype%[%Symbol.iterator%] is
    /// %Array.prototype.values%, %ArrayIteratorPrototype%.next is the built-in one, and no prototype of the array iterators
    /// has a return method. The objects are flagged as dependencies of the inline caches: a change invalidates the result.
    /// </summary>
    public bool IsArrayIterationIntact()
    {
        var epoch = InlineCache.Epoch;
        if (epoch != _arrayIterationEpoch)
        {
            _arrayIterationShapeIntact = ComputeArrayIterationShape();
            _arrayIterationEpoch = epoch;
        }

        return _arrayIterationShapeIntact
            && ReferenceEquals(ArrayPrototype.GetValueForCache(_arrayIteratorMethodPosition, _arrayIteratorMethodInTable).Cell, ArrayPrototypeValues)
            && ReferenceEquals(ArrayIteratorPrototype.GetValueForCache(_arrayIteratorNextPosition, _arrayIteratorNextInTable).Cell, ArrayIteratorNext.Cell);
    }

    /// <summary>Finds the iteration method and the next method (data properties), and checks that no prototype of the array iterators has a return method.</summary>
    private bool ComputeArrayIterationShape()
    {
        if (!TryFindOwnData(ArrayPrototype, new PropertyKey(WellKnownSymbols.Iterator), out _arrayIteratorMethodPosition, out _arrayIteratorMethodInTable))
            return false;

        if (!TryFindOwnData(ArrayIteratorPrototype, new PropertyKey("next"), out _arrayIteratorNextPosition, out _arrayIteratorNextInTable))
            return false;

        var returnKey = new PropertyKey("return");
        for (var prototype = ArrayIteratorPrototype; prototype is not null; prototype = prototype.PrototypeSlot)
        {
            prototype.MarkInlineCachePrototype();
            if (prototype.IsExotic || prototype.TryFindOwnForCache(returnKey, out _, out _))
                return false;
        }

        return true;

        static bool TryFindOwnData(JsObject obj, in PropertyKey key, out int position, out bool inTable)
        {
            obj.MarkInlineCacheHolder();
            inTable = obj.Map is PropertyTable;
            return obj.TryFindOwnForCache(key, out position, out var flags) && (flags & PropertyFlags.Accessor) == 0;
        }
    }
}
