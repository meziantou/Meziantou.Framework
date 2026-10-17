using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A method, a constructor, an operator, a destructor, or an accessor of a property or an event.</summary>
public sealed class PublicApiMethod : PublicApiMember
{
    internal PublicApiMethod(
        string name,
        PublicApiAccessibility accessibility,
        ImmutableArray<PublicApiAttribute> attributes,
        PublicApiMetadataOrigin? origin,
        PublicApiMemberModifiers modifiers,
        ImmutableArray<PublicApiMemberReference> explicitInterfaceImplementations,
        PublicApiMethodKind methodKind,
        PublicApiTypeReference returnType,
        PublicApiRefKind returnRefKind,
        ImmutableArray<PublicApiAttribute> returnAttributes,
        ImmutableArray<PublicApiParameter> parameters,
        ImmutableArray<PublicApiGenericParameter> genericParameters,
        bool isReadOnly,
        bool isExtensionMethod,
        bool isInitOnly)
        : base(name, accessibility, attributes, origin, modifiers, explicitInterfaceImplementations)
    {
        MethodKind = methodKind;
        ReturnType = returnType;
        ReturnRefKind = returnRefKind;
        ReturnAttributes = returnAttributes;
        Parameters = parameters;
        GenericParameters = genericParameters;
        IsReadOnly = isReadOnly;
        IsExtensionMethod = isExtensionMethod;
        IsInitOnly = isInitOnly;
    }

    public override PublicApiSymbolKind Kind => PublicApiSymbolKind.Method;

    public PublicApiMethodKind MethodKind { get; }

    /// <summary>Gets the return type. Constructors and <c>void</c> methods return <c>System.Void</c>.</summary>
    public PublicApiTypeReference ReturnType { get; }

    public PublicApiRefKind ReturnRefKind { get; }

    public ImmutableArray<PublicApiAttribute> ReturnAttributes { get; }

    public ImmutableArray<PublicApiParameter> Parameters { get; }

    public ImmutableArray<PublicApiGenericParameter> GenericParameters { get; }

    public bool IsGenericMethod => !GenericParameters.IsEmpty;

    /// <summary>Gets a value indicating whether the method is a <c>readonly</c> member of a struct.</summary>
    public bool IsReadOnly { get; }

    /// <summary>Gets a value indicating whether the method is a classic extension method (<c>this</c> modifier on the first parameter).</summary>
    public bool IsExtensionMethod { get; }

    /// <summary>Gets a value indicating whether the method is an <c>init</c> accessor.</summary>
    public bool IsInitOnly { get; }

    /// <summary>Gets the property or the event that declares this accessor. It is <see langword="null"/> for other methods.</summary>
    public PublicApiMember? AssociatedSymbol { get; internal set; }

    public bool ReturnsVoid => ReturnRefKind == PublicApiRefKind.None && ReturnType is PublicApiNamedTypeReference returnType && returnType.IsSystemType("Void");
}
