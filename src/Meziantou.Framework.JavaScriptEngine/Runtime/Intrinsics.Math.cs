namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    private JsObject? _math;

    /// <summary>%Math%</summary>
    public JsObject MathObject => _math!;

    partial void InitializeMath() => _math = MathBuiltins.Initialize(this);
}
