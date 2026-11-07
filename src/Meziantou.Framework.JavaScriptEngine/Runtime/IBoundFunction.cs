namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Implemented by the bound function exotic objects.</summary>
internal interface IBoundFunction
{
    /// <summary>Gets the [[BoundTargetFunction]].</summary>
    JsObject BoundTargetFunction { get; }
}
