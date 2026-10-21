namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsFunction? _bigInt;

    /// <summary>%BigInt%</summary>
    public JsFunction BigInt => _bigInt!;

    partial void InitializeBigInt() => _bigInt = BigIntBuiltins.Initialize(this);
}
