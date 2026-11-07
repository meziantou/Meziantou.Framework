namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Boolean%</summary>
    public JsFunction Boolean { get; private set; } = null!;

    partial void InitializeBoolean() => Boolean = BooleanBuiltins.Initialize(this);
}
