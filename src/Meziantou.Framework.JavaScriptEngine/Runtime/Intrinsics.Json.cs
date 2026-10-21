namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _json;

    /// <summary>%JSON%</summary>
    public JsObject Json => _json!;

    partial void InitializeJson() => _json = JsonBuiltins.Initialize(this);
}
