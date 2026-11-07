namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsFunction? _number;
    private JsFunction? _parseInt;
    private JsFunction? _parseFloat;

    /// <summary>%Number%</summary>
    public JsFunction Number => _number!;

    /// <summary>%parseInt%, also Number.parseInt</summary>
    public JsFunction ParseInt => _parseInt!;

    /// <summary>%parseFloat%, also Number.parseFloat</summary>
    public JsFunction ParseFloat => _parseFloat!;

    partial void InitializeNumber()
    {
        // parseInt and parseFloat are the same function objects as Number.parseInt and Number.parseFloat
        _parseInt = GlobalFunctionsBuiltins.CreateParseInt(Realm);
        _parseFloat = GlobalFunctionsBuiltins.CreateParseFloat(Realm);
        _number = NumberBuiltins.Initialize(this);
    }
}
