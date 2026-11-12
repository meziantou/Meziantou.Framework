namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// An instance of a class marked with <see cref="JsExportAttribute"/>: the source generator implements this interface.
/// <see cref="JsRealm.FromDotNet(object?)"/> converts the instance to an object of its JavaScript class, and
/// <see cref="JsEngine.ToDotNet{T}(JsValue)"/> converts the object back to the instance.
/// </summary>
public interface IJsExportable
{
    /// <summary>Gets the JavaScript class of the runtime type of the instance.</summary>
    JsClass JsClass { get; }
}
