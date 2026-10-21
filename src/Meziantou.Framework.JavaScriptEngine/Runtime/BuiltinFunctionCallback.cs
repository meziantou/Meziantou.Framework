namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The implementation of a built-in function. <paramref name="newTarget"/> is null for [[Call]], and the new.target of
/// [[Construct]] otherwise.
/// </summary>
internal delegate JsValue BuiltinFunctionCallback(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget);
