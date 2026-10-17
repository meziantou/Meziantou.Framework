namespace Meziantou.Framework.WebPlatform;

/// <summary>
/// Indicates that a type maps a WebIDL definition (interface, interface mixin, dictionary, enum, callback or typedef).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate, Inherited = false, AllowMultiple = false)]
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
