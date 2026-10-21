namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The native error types (https://tc39.es/ecma262/#sec-native-error-types-used-in-this-standard).</summary>
internal enum ErrorType
{
    Error,
    EvalError,
    RangeError,
    ReferenceError,
    SyntaxError,
    TypeError,
    URIError,
    AggregateError,
}
