namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Number%</summary>
    public JsFunction Number { get; private set; } = null!;

    /// <summary>%parseInt%, also Number.parseInt</summary>
    public JsFunction ParseInt { get; private set; } = null!;

    /// <summary>%parseFloat%, also Number.parseFloat</summary>
    public JsFunction ParseFloat { get; private set; } = null!;

    partial void InitializeNumber()
    {
        // parseInt and parseFloat are the same function objects as Number.parseInt and Number.parseFloat
        ParseInt = Realm.CreateBuiltinFunction("parseInt", 2, GlobalFunctionsBuiltins.ParseInt);
        ParseFloat = Realm.CreateBuiltinFunction("parseFloat", 1, GlobalFunctionsBuiltins.ParseFloat);
        Number = NumberBuiltins.Initialize(this);
    }
}
