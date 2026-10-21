namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Math%</summary>
    public JsObject MathObject { get; private set; } = null!;

    partial void InitializeMath() => MathObject = MathBuiltins.Initialize(this);
}
