using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A property or an indexer.</summary>
/// <remarks>
/// The modifiers (<see cref="PublicApiMember.IsStatic"/>, <see cref="PublicApiMember.IsVirtual"/>...) and the accessibility come from the most accessible accessor.
/// Accessors that are not part of the public API (e.g. a <c>private set</c>) are not exposed.
/// </remarks>
public sealed class PublicApiProperty : PublicApiMember
{
    internal PublicApiProperty(
        string name,
        PublicApiAccessibility accessibility,
        ImmutableArray<PublicApiAttribute> attributes,
        PublicApiMetadataOrigin? origin,
        PublicApiMemberModifiers modifiers,
        ImmutableArray<PublicApiMemberReference> explicitInterfaceImplementations,
        PublicApiTypeReference type,
        PublicApiRefKind refKind,
        ImmutableArray<PublicApiParameter> parameters,
        PublicApiMethod? getMethod,
        PublicApiMethod? setMethod,
        bool isRequired,
        bool isReadOnly)
        : base(name, accessibility, attributes, origin, modifiers, explicitInterfaceImplementations)
    {
        Type = type;
        RefKind = refKind;
        Parameters = parameters;
        GetMethod = getMethod;
        SetMethod = setMethod;
        IsRequired = isRequired;
        IsReadOnly = isReadOnly;
    }

    public override PublicApiSymbolKind Kind => PublicApiSymbolKind.Property;

    public PublicApiTypeReference Type { get; }

    /// <summary>Gets the ref kind of a property that returns by reference (e.g. <c>ref int Value { get; }</c>).</summary>
    public PublicApiRefKind RefKind { get; }

    /// <summary>Gets the parameters of an indexer. It is empty for other properties.</summary>
    public ImmutableArray<PublicApiParameter> Parameters { get; }

    public bool IsIndexer => !Parameters.IsEmpty;

    public PublicApiMethod? GetMethod { get; }

    public PublicApiMethod? SetMethod { get; }

    public bool IsRequired { get; }

    /// <summary>Gets a value indicating whether all the accessors of the property are <c>readonly</c> struct members.</summary>
    public bool IsReadOnly { get; }

    /// <summary>Gets a value indicating whether the property has an <c>init</c> accessor.</summary>
    public bool IsInitOnly => SetMethod?.IsInitOnly is true;
}
