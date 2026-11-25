using System.Diagnostics;

namespace Meziantou.Framework.WebPlatform;

/// <summary>
/// Indicates that a public member of a type annotated with <see cref="IdlInterfaceAttribute"/> is a .NET addition that has no WebIDL counterpart.
/// </summary>
/// <remarks>
/// The attribute is only emitted when <c>WEBPLATFORM_IDL_METADATA</c> is defined: the build that SpecGen and the IDL coverage
/// tests reference by reflection defines it (<c>WebPlatformIdlMetadata=true</c>), the published assembly does not carry it.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Event, Inherited = false, AllowMultiple = false)]
[Conditional("WEBPLATFORM_IDL_METADATA")]
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
