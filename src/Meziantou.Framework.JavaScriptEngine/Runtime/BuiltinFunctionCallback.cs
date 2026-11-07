namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The implementation of a built-in function. <paramref name="newTarget"/> is null for [[Call]], and the new.target of
/// [[Construct]] otherwise.
/// </summary>
internal delegate JsValue BuiltinFunctionCallback(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget);

/// <summary>
/// The step of a built-in next method of an iterator (%ArrayIteratorPrototype%.next...) without its result object: true
/// and the value, or false when the iterator is done. Calling it is the same as calling the function, then reading the
/// done and value properties of the result (own data properties of a new object: nothing is observable).
/// </summary>
internal delegate bool NativeIteratorStep(JsBuiltinFunction function, JsValue thisValue, out JsValue value);
