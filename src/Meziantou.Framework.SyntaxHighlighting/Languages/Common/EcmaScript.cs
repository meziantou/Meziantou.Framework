namespace Meziantou.Framework.SyntaxHighlighting.Languages.Common;

/// <summary>The ECMAScript word lists shared by the JavaScript, TypeScript and CoffeeScript grammars (highlight.js's <c>lib/ecmascript.js</c>).</summary>
internal static class EcmaScript
{
    public const string IdentRe = "[A-Za-z$_][0-9A-Za-z$_]*";

    public static readonly string[] Keywords =
    [
        "as", "in", "of", "if", "for", "while", "finally", "var", "new", "function", "do", "return", "void", "else", "break",
        "catch", "instanceof", "with", "throw", "case", "default", "try", "switch", "continue", "typeof", "delete", "let",
        "yield", "const", "class", "debugger", "async", "await", "static", "import", "from", "export", "extends", "using",
    ];

    public static readonly string[] Literals = ["true", "false", "null", "undefined", "NaN", "Infinity"];

    public static readonly string[] Types =
    [
        "Object", "Function", "Boolean", "Symbol", "Math", "Date", "Number", "BigInt", "String", "RegExp", "Array",
        "Float32Array", "Float64Array", "Int8Array", "Uint8Array", "Uint8ClampedArray", "Int16Array", "Int32Array",
        "Uint16Array", "Uint32Array", "BigInt64Array", "BigUint64Array", "Set", "Map", "WeakSet", "WeakMap", "ArrayBuffer",
        "SharedArrayBuffer", "Atomics", "DataView", "JSON", "Promise", "Generator", "GeneratorFunction", "AsyncFunction",
        "Reflect", "Proxy", "Intl", "WebAssembly",
    ];

    public static readonly string[] ErrorTypes =
    [
        "Error", "EvalError", "InternalError", "RangeError", "ReferenceError", "SyntaxError", "TypeError", "URIError",
    ];

    public static readonly string[] BuiltInGlobals =
    [
        "setInterval", "setTimeout", "clearInterval", "clearTimeout", "require", "exports", "eval", "isFinite", "isNaN",
        "parseFloat", "parseInt", "decodeURI", "decodeURIComponent", "encodeURI", "encodeURIComponent", "escape", "unescape",
    ];

    public static readonly string[] BuiltIns = [.. BuiltInGlobals, .. Types, .. ErrorTypes];

    public static readonly string[] BuiltInVariables =
    [
        "arguments", "this", "super", "console", "window", "document", "localStorage", "sessionStorage", "module", "global",
    ];
}
