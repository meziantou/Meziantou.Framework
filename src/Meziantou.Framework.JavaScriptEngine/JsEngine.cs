namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>An ECMAScript agent: it owns the realms, the job queue and the execution limits of the scripts it runs.</summary>
public sealed class JsEngine : IDisposable
{
    /// <summary>Releases the resources of the engine.</summary>
    public void Dispose()
    {
    }
}
