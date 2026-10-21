using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>A Script Record (https://tc39.es/ecma262/#sec-script-records): the [[ScriptOrModule]] of the code it runs.</summary>
internal sealed class ScriptRecord(JsRealm realm, SourceText source, object? hostDefined, bool mutedErrors) : IScriptOrModule
{
    public JsRealm Realm { get; } = realm;

    public SourceText Source { get; } = source;

    public string? Url => Source.Url;

    /// <summary>Gets the data of the host associated with the script ([[HostDefined]]).</summary>
    public object? HostDefined { get; } = hostDefined;

    /// <summary>Gets whether the errors of the script are muted (a cross-origin script without CORS).</summary>
    public bool MutedErrors { get; } = mutedErrors;
}
