namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    partial void InitializeFunction() => SetFunctionConstructor(FunctionBuiltins.Initialize(this));
}
