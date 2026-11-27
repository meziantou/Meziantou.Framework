namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>The handler of the legacy platform objects, for the adapters of the library.</summary>
internal interface ILegacyPlatformObjectHandler<TValue> : IJsHostObjectHandler<TValue>
    where TValue : class
{
    /// <summary>
    /// [[GetOwnProperty]] when the caller checked that the object has no ordinary own property of the key. An own property
    /// hides the named property of the same name, and is the result when there is no indexed or named property: an adapter
    /// that looks for it first saves the lookup of the named property visibility.
    /// </summary>
    bool TryGetOwnPropertyWithoutOrdinaryProperty(TValue obj, TValue key, out JsPropertyDescriptor<TValue> descriptor);
}
