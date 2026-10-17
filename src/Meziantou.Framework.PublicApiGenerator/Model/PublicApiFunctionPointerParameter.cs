namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A parameter of a <see cref="PublicApiFunctionPointerTypeReference"/>.</summary>
public sealed class PublicApiFunctionPointerParameter
{
    internal PublicApiFunctionPointerParameter(PublicApiRefKind refKind, PublicApiTypeReference type)
    {
        RefKind = refKind;
        Type = type;
    }

    public PublicApiRefKind RefKind { get; }

    public PublicApiTypeReference Type { get; }
}
