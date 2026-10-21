namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Symbol constructor and Symbol.prototype (https://tc39.es/ecma262/#sec-symbol-objects).</summary>
internal static class SymbolBuiltins
{
    public static JsBuiltinFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.SymbolPrototype;
        var constructor = realm.CreateBuiltinFunction("Symbol", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);

        realm.DefineMethod(constructor, "for", 1, For);
        realm.DefineMethod(constructor, "keyFor", 1, KeyFor);
        constructor.DefineBuiltIn("asyncDispose", WellKnownSymbols.AsyncDispose, PropertyFlags.None);
        constructor.DefineBuiltIn("asyncIterator", WellKnownSymbols.AsyncIterator, PropertyFlags.None);
        constructor.DefineBuiltIn("dispose", WellKnownSymbols.Dispose, PropertyFlags.None);
        constructor.DefineBuiltIn("hasInstance", WellKnownSymbols.HasInstance, PropertyFlags.None);
        constructor.DefineBuiltIn("isConcatSpreadable", WellKnownSymbols.IsConcatSpreadable, PropertyFlags.None);
        constructor.DefineBuiltIn("iterator", WellKnownSymbols.Iterator, PropertyFlags.None);
        constructor.DefineBuiltIn("match", WellKnownSymbols.Match, PropertyFlags.None);
        constructor.DefineBuiltIn("matchAll", WellKnownSymbols.MatchAll, PropertyFlags.None);
        constructor.DefineBuiltIn("replace", WellKnownSymbols.Replace, PropertyFlags.None);
        constructor.DefineBuiltIn("search", WellKnownSymbols.Search, PropertyFlags.None);
        constructor.DefineBuiltIn("species", WellKnownSymbols.Species, PropertyFlags.None);
        constructor.DefineBuiltIn("split", WellKnownSymbols.Split, PropertyFlags.None);
        constructor.DefineBuiltIn("toPrimitive", WellKnownSymbols.ToPrimitive, PropertyFlags.None);
        constructor.DefineBuiltIn("toStringTag", WellKnownSymbols.ToStringTag, PropertyFlags.None);
        constructor.DefineBuiltIn("unscopables", WellKnownSymbols.Unscopables, PropertyFlags.None);

        prototype.DefineBuiltIn("constructor", constructor);
        realm.DefineAccessor(prototype, "description", GetDescription, setter: null);
        realm.DefineMethod(prototype, "toString", 0, ToString);
        realm.DefineMethod(prototype, "valueOf", 0, ValueOf);
        realm.DefineMethod(prototype, WellKnownSymbols.ToPrimitive, 1, ToPrimitive, PropertyFlags.Configurable);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Symbol", PropertyFlags.Configurable);

        intrinsics.AddGlobal("Symbol", constructor);
        return constructor;
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>Symbol([description])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is not null)
            return ThrowHelper.ThrowTypeError<JsValue>("Symbol is not a constructor");

        var description = Arg(arguments, 0);
        var descriptionString = description.IsUndefined ? null : JsOperations.ToString(description);
        return new JsSymbol(descriptionString);
    }

    /// <summary>Symbol.for(key)</summary>
    private static JsValue For(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var key = JsOperations.ToString(Arg(arguments, 0));
        return function.Realm.Engine.GetOrCreateRegisteredSymbol(key);
    }

    /// <summary>Symbol.keyFor(sym)</summary>
    private static JsValue KeyFor(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (Arg(arguments, 0).Cell is not JsSymbol symbol)
            return ThrowHelper.ThrowTypeError<JsValue>($"{JsOperations.DescribeForError(Arg(arguments, 0))} is not a symbol");

        // KeyForSymbol(sym): the key of a registered symbol is its description
        if (symbol.IsRegistered)
            return symbol.Description!;

        return JsValue.Undefined;
    }

    /// <summary>ThisSymbolValue(value)</summary>
    private static JsSymbol ThisSymbolValue(JsValue value, string methodName)
    {
        if (value.Cell is JsSymbol symbol)
            return symbol;

        if (value.Cell is JsPrimitiveObject { Class: ObjectClass.Symbol } obj)
            return obj.PrimitiveValue.AsSymbol();

        return ThrowHelper.ThrowTypeError<JsSymbol>($"{methodName} requires that 'this' be a Symbol");
    }

    /// <summary>get Symbol.prototype.description</summary>
    private static JsValue GetDescription(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var symbol = ThisSymbolValue(thisValue, "Symbol.prototype.description");
        return symbol.Description is { } description ? description : JsValue.Undefined;
    }

    /// <summary>Symbol.prototype.toString()</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisSymbolValue(thisValue, "Symbol.prototype.toString").ToString();
    }

    /// <summary>Symbol.prototype.valueOf()</summary>
    private static JsValue ValueOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisSymbolValue(thisValue, "Symbol.prototype.valueOf");
    }

    /// <summary>Symbol.prototype[@@toPrimitive](hint)</summary>
    private static JsValue ToPrimitive(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisSymbolValue(thisValue, "Symbol.prototype [ @@toPrimitive ]");
    }
}
