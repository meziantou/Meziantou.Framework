namespace Meziantou.Framework.WebPlatform;

/// <summary>
/// Indicates that a public member of a type annotated with <see cref="IdlInterfaceAttribute"/> is a .NET addition that has no WebIDL counterpart.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Event, Inherited = false, AllowMultiple = false)]
public sealed class NotInIdlAttribute : Attribute
{
    /// <summary>Creates an attribute for a member that has no WebIDL counterpart.</summary>
    public NotInIdlAttribute()
    {
    }

    /// <summary>
    /// Creates an attribute for a member that has no WebIDL counterpart, with the reason why it exists.
    /// </summary>
    public NotInIdlAttribute(string reason)
    {
        Reason = reason;
    }

    /// <summary>Gets the reason why this member exists.</summary>
    public string? Reason { get; }
}
