namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Compares type references structurally, including tuple element names. The assembly that declares a type is not compared,
/// because types are commonly forwarded between assemblies (e.g. netstandard and System.Runtime).
/// </summary>
internal sealed class PublicApiTypeReferenceComparer : IEqualityComparer<PublicApiTypeReference>
{
    public static PublicApiTypeReferenceComparer Default { get; } = new(ignoreNullability: false);

    public static PublicApiTypeReferenceComparer IgnoreNullability { get; } = new(ignoreNullability: true);

    private readonly bool _ignoreNullability;

    private PublicApiTypeReferenceComparer(bool ignoreNullability)
    {
        _ignoreNullability = ignoreNullability;
    }

    public bool Equals(PublicApiTypeReference? x, PublicApiTypeReference? y)
    {
        if (ReferenceEquals(x, y))
            return true;

        if (x is null || y is null)
            return false;

        if (x.Kind != y.Kind)
            return false;

        if (!_ignoreNullability && x.NullableAnnotation != y.NullableAnnotation)
            return false;

        return (x, y) switch
        {
            (PublicApiNamedTypeReference left, PublicApiNamedTypeReference right) => EqualsNamed(left, right),
            (PublicApiTypeParameterReference left, PublicApiTypeParameterReference right) =>
                left.Ordinal == right.Ordinal &&
                left.IsMethodTypeParameter == right.IsMethodTypeParameter &&
                string.Equals(left.Name, right.Name, StringComparison.Ordinal),
            (PublicApiArrayTypeReference left, PublicApiArrayTypeReference right) =>
                left.Rank == right.Rank &&
                left.IsSZArray == right.IsSZArray &&
                Equals(left.ElementType, right.ElementType),
            (PublicApiPointerTypeReference left, PublicApiPointerTypeReference right) => Equals(left.ElementType, right.ElementType),
            (PublicApiFunctionPointerTypeReference left, PublicApiFunctionPointerTypeReference right) => EqualsFunctionPointer(left, right),
            _ => false,
        };
    }

    public int GetHashCode(PublicApiTypeReference obj)
    {
        // Nullable annotations and tuple element names are not part of the hash code, so both comparers can share it
        var hash = new HashCode();
        hash.Add(obj.Kind);
        switch (obj)
        {
            case PublicApiNamedTypeReference named:
                hash.Add(named.Namespace, StringComparer.Ordinal);
                hash.Add(named.MetadataName, StringComparer.Ordinal);
                if (named.ContainingType is not null)
                {
                    hash.Add(GetHashCode(named.ContainingType));
                }

                foreach (var typeArgument in named.TypeArguments)
                {
                    hash.Add(GetHashCode(typeArgument));
                }

                break;

            case PublicApiTypeParameterReference typeParameter:
                hash.Add(typeParameter.Ordinal);
                hash.Add(typeParameter.IsMethodTypeParameter);
                break;

            case PublicApiArrayTypeReference array:
                hash.Add(array.Rank);
                hash.Add(GetHashCode(array.ElementType));
                break;

            case PublicApiPointerTypeReference pointer:
                hash.Add(GetHashCode(pointer.ElementType));
                break;

            case PublicApiFunctionPointerTypeReference functionPointer:
                hash.Add(functionPointer.CallingConvention);
                hash.Add(functionPointer.Parameters.Length);
                hash.Add(GetHashCode(functionPointer.ReturnType));
                break;
        }

        return hash.ToHashCode();
    }

    private bool EqualsNamed(PublicApiNamedTypeReference x, PublicApiNamedTypeReference y)
    {
        if (!string.Equals(x.Namespace, y.Namespace, StringComparison.Ordinal) ||
            !string.Equals(x.MetadataName, y.MetadataName, StringComparison.Ordinal) ||
            x.TypeArguments.Length != y.TypeArguments.Length)
        {
            return false;
        }

        if (!Equals(x.ContainingType, y.ContainingType))
            return false;

        if (!x.TupleElementNames.SequenceEqual(y.TupleElementNames, StringComparer.Ordinal))
            return false;

        for (var i = 0; i < x.TypeArguments.Length; i++)
        {
            if (!Equals(x.TypeArguments[i], y.TypeArguments[i]))
                return false;
        }

        return true;
    }

    private bool EqualsFunctionPointer(PublicApiFunctionPointerTypeReference x, PublicApiFunctionPointerTypeReference y)
    {
        if (x.CallingConvention != y.CallingConvention ||
            x.ReturnRefKind != y.ReturnRefKind ||
            x.Parameters.Length != y.Parameters.Length ||
            !Equals(x.ReturnType, y.ReturnType))
        {
            return false;
        }

        for (var i = 0; i < x.Parameters.Length; i++)
        {
            if (x.Parameters[i].RefKind != y.Parameters[i].RefKind || !Equals(x.Parameters[i].Type, y.Parameters[i].Type))
                return false;
        }

        return true;
    }
}
