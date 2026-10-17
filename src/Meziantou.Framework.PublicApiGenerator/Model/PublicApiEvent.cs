using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>An event.</summary>
/// <remarks>The modifiers and the accessibility come from the <c>add</c> accessor.</remarks>
public sealed class PublicApiEvent : PublicApiMember
{
    internal PublicApiEvent(
        string name,
        PublicApiAccessibility accessibility,
        ImmutableArray<PublicApiAttribute> attributes,
        PublicApiMetadataOrigin? origin,
        PublicApiMemberModifiers modifiers,
        ImmutableArray<PublicApiMemberReference> explicitInterfaceImplementations,
        PublicApiTypeReference type,
        PublicApiMethod addMethod,
        PublicApiMethod? removeMethod,
        PublicApiMethod? raiseMethod)
        : base(name, accessibility, attributes, origin, modifiers, explicitInterfaceImplementations)
    {
        Type = type;
        AddMethod = addMethod;
        RemoveMethod = removeMethod;
        RaiseMethod = raiseMethod;
    }

    public override PublicApiSymbolKind Kind => PublicApiSymbolKind.Event;

    public PublicApiTypeReference Type { get; }

    public PublicApiMethod AddMethod { get; }

    public PublicApiMethod? RemoveMethod { get; }

    public PublicApiMethod? RaiseMethod { get; }
}
