namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Symbol%</summary>
    public JsFunction Symbol { get; private set; } = null!;

    partial void InitializeSymbol() => Symbol = SymbolBuiltins.Initialize(this);
}
