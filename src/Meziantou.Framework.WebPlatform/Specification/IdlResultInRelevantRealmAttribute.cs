namespace Meziantou.Framework.WebPlatform;

/// <summary>
/// Indicates that the platform object returned by an operation is created in the relevant realm of the object the operation
/// is called on (e.g. "a new Blob object in this's relevant realm", https://w3c.github.io/FileAPI/#dfn-slice), instead of in
/// the current realm (the realm of the function, which differs when the function of another realm is called).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
internal sealed class IdlResultInRelevantRealmAttribute : Attribute
{
}
