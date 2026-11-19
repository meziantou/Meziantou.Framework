namespace Meziantou.Framework.WebPlatform.Scripting;

// The conversions of the values of the type any that the .NET APIs read themselves (e.g. the descriptions of the CanvasFilter
// objects), with the primitives of the engine. The values that are not values of the engine (the .NET collections of the .NET
// APIs) are converted as .NET values.
public sealed partial class WebIdlRealm<TAdapter, TValue> : IScriptValueOperations
{
    bool IScriptValueOperations.IsNullOrUndefined(object? value) => value is null || (TryGetScriptValue(value, out var jsValue) ? IsNullOrUndefined(jsValue) : DotNetScriptValueOperations.Instance.IsNullOrUndefined(value));

    bool IScriptValueOperations.IsObject(object? value) => TryGetScriptValue(value, out var jsValue) ? Adapter.IsObject(jsValue) : DotNetScriptValueOperations.Instance.IsObject(value);

    object? IScriptValueOperations.UnwrapPlatformObject(object? value) => value is TValue jsValue && Adapter.IsValue(jsValue) && Adapter.TryGetPlatformObject(jsValue, out var target) ? target : value;

    bool IScriptValueOperations.IsBoolean(object? value) => TryGetScriptValue(value, out var jsValue) ? Adapter.IsBoolean(jsValue) : DotNetScriptValueOperations.Instance.IsBoolean(value);

    bool IScriptValueOperations.IsNumber(object? value) => TryGetScriptValue(value, out var jsValue) ? Adapter.IsNumber(jsValue) : DotNetScriptValueOperations.Instance.IsNumber(value);

    List<object?>? IScriptValueOperations.TryToSequence(object? value)
    {
        if (!TryGetScriptValue(value, out var jsValue))
            return DotNetScriptValueOperations.Instance.TryToSequence(value);

        if (!Adapter.IsObject(jsValue))
            return null;

        var method = Adapter.Get(jsValue, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.Iterator));
        if (!Adapter.IsCallable(method))
            return null;

        // ToSequence uses the method that is already got (@@iterator is got only once)
        _iteratorMethodTarget = jsValue;
        _iteratorMethod = method;
        return ToSequence(jsValue, static (_, element) => (object?)element);
    }

    List<KeyValuePair<string, object?>> IScriptValueOperations.ToRecord(object? value)
    {
        if (!TryGetScriptValue(value, out var jsValue))
            return DotNetScriptValueOperations.Instance.ToRecord(value);

        if (IsNullOrUndefined(jsValue))
            return [];

        return ToRecord(jsValue, static (realm, key) => realm.ToDomString(key), static (_, element) => (object?)element);
    }

    double IScriptValueOperations.ToNumber(object? value) => TryGetScriptValue(value, out var jsValue) ? Adapter.ToNumber(jsValue) : DotNetScriptValueOperations.Instance.ToNumber(value);

    bool IScriptValueOperations.ToBoolean(object? value) => TryGetScriptValue(value, out var jsValue) ? Adapter.ToBoolean(jsValue) : DotNetScriptValueOperations.Instance.ToBoolean(value);

    string IScriptValueOperations.ToDomString(object? value) => TryGetScriptValue(value, out var jsValue) ? ToDomString(jsValue) : DotNetScriptValueOperations.Instance.ToDomString(value);

    /// <summary>
    /// Returns the value of the engine of a value: a value of the engine, or a .NET primitive value (null, a string, a boolean
    /// or a number). Returns false for the other .NET values (e.g. a dictionary of a .NET API), which are converted as .NET values.
    /// </summary>
    private bool TryGetScriptValue(object? value, out TValue result)
    {
        switch (value)
        {
            case null:
                result = Adapter.Null;
                return true;

            case TValue jsValue when Adapter.IsValue(jsValue):
                result = jsValue;
                return true;

            case string or bool or double or float or int or uint or long or ulong or short or ushort or byte or sbyte or decimal:
                result = ToJs(value);
                return true;

            default:
                result = default!;
                return false;
        }
    }
}
