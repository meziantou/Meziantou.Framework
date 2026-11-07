namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The type of a JavaScript value (https://tc39.es/ecma262/#sec-ecmascript-language-types).</summary>
public enum JsValueKind
{
    /// <summary>The <c>undefined</c> value.</summary>
    Undefined,

    /// <summary>The <c>null</c> value.</summary>
    Null,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>A double-precision floating-point number.</summary>
    Number,

    /// <summary>A sequence of UTF-16 code units.</summary>
    String,

    /// <summary>A unique value used as a property key.</summary>
    Symbol,

    /// <summary>An arbitrary-precision integer.</summary>
    BigInt,

    /// <summary>An object (including the functions).</summary>
    Object,
}
