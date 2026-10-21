namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The [[Status]] of a module (https://tc39.es/ecma262/#sec-cyclic-module-records).</summary>
public enum JsModuleStatus
{
    /// <summary>The module is created; its dependencies are not loaded yet.</summary>
    New,

    /// <summary>The module and its dependencies are loaded.</summary>
    Unlinked,

    /// <summary>The module is being linked.</summary>
    Linking,

    /// <summary>The module and its dependencies are linked: the module can be evaluated.</summary>
    Linked,

    /// <summary>The module is being evaluated.</summary>
    Evaluating,

    /// <summary>The module waits for an asynchronous dependency, or runs its top-level await.</summary>
    EvaluatingAsync,

    /// <summary>The module is evaluated (successfully, or with an error).</summary>
    Evaluated,
}
