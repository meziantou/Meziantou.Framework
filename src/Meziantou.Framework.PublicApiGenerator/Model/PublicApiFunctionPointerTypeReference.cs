using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A reference to a function pointer type (e.g. <c>delegate*&lt;int, void&gt;</c>).</summary>
public sealed class PublicApiFunctionPointerTypeReference : PublicApiTypeReference
{
    internal PublicApiFunctionPointerTypeReference(
        PublicApiCallingConvention callingConvention,
        PublicApiTypeReference returnType,
        PublicApiRefKind returnRefKind,
        ImmutableArray<PublicApiFunctionPointerParameter> parameters)
        : base(PublicApiNullableAnnotation.NotAnnotated)
    {
        CallingConvention = callingConvention;
        ReturnType = returnType;
        ReturnRefKind = returnRefKind;
        Parameters = parameters;
    }

    public override PublicApiTypeReferenceKind Kind => PublicApiTypeReferenceKind.FunctionPointerType;

    /// <summary>Gets the calling convention encoded in the signature. Calling conventions encoded as custom modifiers are not decoded.</summary>
    public PublicApiCallingConvention CallingConvention { get; }

    public PublicApiTypeReference ReturnType { get; }

    public PublicApiRefKind ReturnRefKind { get; }

    public ImmutableArray<PublicApiFunctionPointerParameter> Parameters { get; }
}
