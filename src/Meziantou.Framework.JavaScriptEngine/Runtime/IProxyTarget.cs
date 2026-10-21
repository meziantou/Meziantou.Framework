namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Implemented by the proxy exotic objects (ObjectClass.Proxy).</summary>
internal interface IProxyTarget
{
    /// <summary>Gets the [[ProxyTarget]], or throws a TypeError when the proxy is revoked.</summary>
    JsObject GetProxyTargetOrThrow();
}
