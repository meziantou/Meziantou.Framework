using Meziantou.Framework.JavaScriptEngine.Interpreter;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A script or a module: the [[ScriptOrModule]] of the running code (https://tc39.es/ecma262/#sec-getactivescriptormodule).</summary>
public abstract class JsScriptOrModule : IScriptOrModule
{
    private protected JsScriptOrModule(JsRealm realm, string? url, object? hostDefined)
    {
        Realm = realm;
        Url = url;
        HostDefined = hostDefined;
    }

    /// <summary>Gets the realm the script or the module was created in ([[Realm]]).</summary>
    public JsRealm Realm { get; }

    /// <summary>Gets the URL (or the file name) of the script or the module.</summary>
    public string? Url { get; }

    /// <summary>Gets the data of the host associated with the script or the module ([[HostDefined]]).</summary>
    public object? HostDefined { get; }
}

/// <summary>A Script Record (https://tc39.es/ecma262/#sec-script-records).</summary>
public sealed class JsScript : JsScriptOrModule
{
    internal JsScript(JsRealm realm, Text.SourceText source, object? hostDefined, bool mutedErrors)
        : base(realm, source.Url, hostDefined)
    {
        Source = source;
        MutedErrors = mutedErrors;
    }

    internal Text.SourceText Source { get; }

    /// <summary>Gets whether the errors of the script are muted (a cross-origin script fetched without CORS).</summary>
    public bool MutedErrors { get; }
}
