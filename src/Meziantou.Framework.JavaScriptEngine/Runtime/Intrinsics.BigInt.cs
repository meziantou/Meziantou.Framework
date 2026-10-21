namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%BigInt%</summary>
    public JsFunction BigInt { get; private set; } = null!;

    partial void InitializeBigInt() => BigInt = BigIntBuiltins.Initialize(this);
}
