namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    /// <summary>%Reflect%</summary>
    public JsObject Reflect { get; private set; } = null!;

    partial void InitializeReflect() => Reflect = ReflectBuiltins.Initialize(this);
}
