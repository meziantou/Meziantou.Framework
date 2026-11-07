namespace Meziantou.Framework.WebPlatform;

/// <summary>
/// Indicates that a member maps a WebIDL member (attribute, operation, constructor, constant, getter/setter or enum value).
/// A member can map several WebIDL members, for instance an indexer that implements both an indexed and a named getter.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Event, Inherited = false, AllowMultiple = true)]
public sealed class IdlMemberAttribute : Attribute
{
    /// <summary>Creates an attribute with the WebIDL name of the member and the URL of its specification.</summary>
    public IdlMemberAttribute(string name, string specUrl)
    {
        Name = name;
        SpecUrl = specUrl;
    }

    /// <summary>Gets the name of the member exactly as written in the WebIDL.</summary>
    public string Name { get; }

    /// <summary>Gets the URL of the member in the specification.</summary>
    public string SpecUrl { get; }
}
