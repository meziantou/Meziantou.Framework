namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A platform object that is created in the realm of a given window rather than in the current realm (e.g. an object
/// created by the user agent for a window other than the one of the running script).
/// </summary>
internal interface IRealmBoundPlatformObject
{
    /// <summary>The window of the realm of the object, or null for the current realm.</summary>
    Window? RealmWindow { get; }
}
