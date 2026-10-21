namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Proxy%</summary>
    public JsFunction Proxy { get; private set; } = null!;

    partial void InitializeProxy() => Proxy = ProxyBuiltins.Initialize(this);
}
