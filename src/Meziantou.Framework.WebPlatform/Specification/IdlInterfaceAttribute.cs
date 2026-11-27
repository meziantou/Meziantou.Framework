using System.Diagnostics;

namespace Meziantou.Framework.WebPlatform;

/// <summary>
/// Indicates that a type maps a WebIDL definition (interface, interface mixin, dictionary, enum, callback or typedef).
/// </summary>
/// <remarks>
/// The attribute is only emitted when <c>WEBPLATFORM_IDL_METADATA</c> is defined: the build that SpecGen and the IDL coverage
/// tests reference by reflection defines it (<c>WebPlatformIdlMetadata=true</c>), the published assembly does not carry it.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate, Inherited = false, AllowMultiple = false)]
[Conditional("WEBPLATFORM_IDL_METADATA")]
public sealed class IdlInterfaceAttribute : Attribute
{
    /// <summary>Creates an attribute with the WebIDL name of the definition and the URL of its specification.</summary>
    public IdlInterfaceAttribute(string name, string specUrl)
    {
        Name = name;
        SpecUrl = specUrl;
    }

    /// <summary>Gets the name of the definition exactly as written in the WebIDL.</summary>
    public string Name { get; }

    /// <summary>Gets the URL of the definition in the specification.</summary>
    public string SpecUrl { get; }
}
