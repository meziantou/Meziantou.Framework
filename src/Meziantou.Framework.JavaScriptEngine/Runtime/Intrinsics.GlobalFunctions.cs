namespace Meziantou.Framework.JavaScriptEngine;

internal sealed partial class Intrinsics
{
    partial void InitializeGlobalFunctions() => GlobalFunctionsBuiltins.Initialize(this);
}
