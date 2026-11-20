namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The kind of code parsed, compiled or run (the <c>js.code.kind</c> tag of the diagnostics).</summary>
internal enum JsCodeKind
{
    Script,
    Module,
    Eval,
    Function,
    LazyFunction,
    Jit,
}
