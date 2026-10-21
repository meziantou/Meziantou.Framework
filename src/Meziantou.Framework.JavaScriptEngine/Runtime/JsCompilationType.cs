namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The kind of a compilation of a string by the scripts (HostEnsureCanCompileStrings).</summary>
public enum JsCompilationType
{
    /// <summary>A direct eval.</summary>
    DirectEval,

    /// <summary>An indirect eval.</summary>
    IndirectEval,

    /// <summary>The Function constructor (or GeneratorFunction, AsyncFunction, AsyncGeneratorFunction).</summary>
    Function,
}
