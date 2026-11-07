namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    partial void InitializeObject() => SetObjectConstructor(ObjectBuiltins.Initialize(this));
}
