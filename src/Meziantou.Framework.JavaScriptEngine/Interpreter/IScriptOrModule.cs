namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>The [[ScriptOrModule]] of an execution context: a Script Record or a Module Record.</summary>
internal interface IScriptOrModule
{
    JsRealm Realm { get; }

    /// <summary>Gets the URL (or the file name) of the script or the module.</summary>
    string? Url { get; }

    object? HostDefined { get; }
}
