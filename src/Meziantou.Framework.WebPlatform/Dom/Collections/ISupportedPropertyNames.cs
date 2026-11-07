namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An object with named properties (https://webidl.spec.whatwg.org/#dfn-support-named-properties) that can tell whether a
/// name is one of its supported property names without computing all of them: the bindings check it on each property access.
/// </summary>
internal interface ISupportedPropertyNames
{
    /// <summary>Returns whether the name is one of the supported property names of the object.</summary>
    bool IsSupportedPropertyName(string name);
}
