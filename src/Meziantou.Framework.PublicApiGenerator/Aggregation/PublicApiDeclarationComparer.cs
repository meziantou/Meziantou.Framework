using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Compares the declarations of two symbols using the structured model. Members and nested types are not part of the declaration of a type,
/// and the metadata provenance (tokens, MVID) is ignored.
/// </summary>
internal static class PublicApiDeclarationComparer
{
    // These attributes encode parts of the declaration that are already compared (nullable annotations, tuple element names, modifiers...)
    private static readonly HashSet<string> IgnoredAttributes = new(StringComparer.Ordinal)
    {
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.NullablePublicOnlyAttribute",
        "System.Runtime.CompilerServices.TupleElementNamesAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Runtime.CompilerServices.RequiresLocationAttribute",
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Runtime.CompilerServices.RequiredMemberAttribute",
        "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.ParamCollectionAttribute",
        "System.ParamArrayAttribute",
        "System.Runtime.CompilerServices.ClosedAttribute",
        "System.Runtime.CompilerServices.IsClosedTypeAttribute",
        PublicApiMetadataReader.RequiresUnsafeAttributeFullName,
    };

    public static PublicApiSymbolDifferences Compare(PublicApiSymbol x, PublicApiSymbol y)
    {
        var differences = PublicApiSymbolDifferences.None;
        if (x.Kind != y.Kind)
            return PublicApiSymbolDifferences.Kind;

        if (x.Accessibility != y.Accessibility)
        {
            differences |= PublicApiSymbolDifferences.Accessibility;
        }

        differences |= CompareAttributes(x.Attributes, y.Attributes);
        differences |= (x, y) switch
        {
            (PublicApiType left, PublicApiType right) => CompareTypes(left, right),
            (PublicApiField left, PublicApiField right) => CompareFields(left, right),
            (PublicApiProperty left, PublicApiProperty right) => CompareProperties(left, right),
            (PublicApiEvent left, PublicApiEvent right) => CompareEvents(left, right),
            (PublicApiMethod left, PublicApiMethod right) => CompareMethods(left, right),
            _ => PublicApiSymbolDifferences.None,
        };

        return differences;
    }

    private static PublicApiSymbolDifferences CompareTypes(PublicApiType x, PublicApiType y)
    {
        if (x.TypeKind != y.TypeKind)
            return PublicApiSymbolDifferences.Kind;

        var differences = PublicApiSymbolDifferences.None;
        if (x.IsStatic != y.IsStatic ||
            x.IsAbstract != y.IsAbstract ||
            x.IsSealed != y.IsSealed ||
            x.IsReadOnly != y.IsReadOnly ||
            x.IsRefLike != y.IsRefLike ||
            x.IsClosed != y.IsClosed ||
            x.IsUnion != y.IsUnion ||
            x.IsNew != y.IsNew)
        {
            differences |= PublicApiSymbolDifferences.Modifiers;
        }

        differences |= CompareGenericParameters(x.GenericParameters, y.GenericParameters);

        var baseTypeDifferences = CompareTypeReference(x.BaseType, y.BaseType);
        var interfaceDifferences = CompareTypeReferenceSets(x.Interfaces, y.Interfaces);
        if ((baseTypeDifferences | interfaceDifferences).HasFlag(PublicApiSymbolDifferences.Signature))
        {
            differences |= PublicApiSymbolDifferences.Inheritance;
        }

        differences |= (baseTypeDifferences | interfaceDifferences) & PublicApiSymbolDifferences.Nullability;

        differences |= CompareTypeReference(x.EnumUnderlyingType, y.EnumUnderlyingType);
        differences |= CompareTypeReferenceLists(x.UnionCaseTypes, y.UnionCaseTypes);
        if (x.DelegateInvokeMethod is not null && y.DelegateInvokeMethod is not null)
        {
            differences |= CompareMethods(x.DelegateInvokeMethod, y.DelegateInvokeMethod) & ~PublicApiSymbolDifferences.Modifiers;
        }

        return differences;
    }

    private static PublicApiSymbolDifferences CompareFields(PublicApiField x, PublicApiField y)
    {
        var differences = CompareMemberModifiers(x, y);
        if (x.IsReadOnly != y.IsReadOnly || x.IsConst != y.IsConst || x.IsVolatile != y.IsVolatile || x.IsRequired != y.IsRequired)
        {
            differences |= PublicApiSymbolDifferences.Modifiers;
        }

        if (x.RefKind != y.RefKind)
        {
            differences |= PublicApiSymbolDifferences.Signature;
        }

        differences |= CompareTypeReference(x.Type, y.Type);
        if (x.HasConstantValue != y.HasConstantValue || !Equals(x.ConstantValue, y.ConstantValue))
        {
            differences |= PublicApiSymbolDifferences.Value;
        }

        return differences;
    }

    private static PublicApiSymbolDifferences CompareProperties(PublicApiProperty x, PublicApiProperty y)
    {
        var differences = CompareMemberModifiers(x, y);
        if (x.IsRequired != y.IsRequired || x.IsReadOnly != y.IsReadOnly)
        {
            differences |= PublicApiSymbolDifferences.Modifiers;
        }

        if (x.RefKind != y.RefKind)
        {
            differences |= PublicApiSymbolDifferences.Signature;
        }

        differences |= CompareTypeReference(x.Type, y.Type);
        differences |= CompareParameters(x.Parameters, y.Parameters);
        differences |= CompareAccessor(x.GetMethod, y.GetMethod);
        differences |= CompareAccessor(x.SetMethod, y.SetMethod);
        return differences;
    }

    private static PublicApiSymbolDifferences CompareEvents(PublicApiEvent x, PublicApiEvent y)
    {
        var differences = CompareMemberModifiers(x, y);
        differences |= CompareTypeReference(x.Type, y.Type);
        differences |= CompareAccessor(x.AddMethod, y.AddMethod);
        differences |= CompareAccessor(x.RemoveMethod, y.RemoveMethod);
        differences |= CompareAccessor(x.RaiseMethod, y.RaiseMethod);
        return differences;
    }

    private static PublicApiSymbolDifferences CompareAccessor(PublicApiMethod? x, PublicApiMethod? y)
    {
        if (x is null || y is null)
            return x is null && y is null ? PublicApiSymbolDifferences.None : PublicApiSymbolDifferences.Signature;

        var differences = PublicApiSymbolDifferences.None;
        if (x.Accessibility != y.Accessibility)
        {
            differences |= PublicApiSymbolDifferences.Accessibility;
        }

        if (x.IsInitOnly != y.IsInitOnly)
        {
            differences |= PublicApiSymbolDifferences.Signature;
        }

        if (x.IsReadOnly != y.IsReadOnly || x.RequiresUnsafe != y.RequiresUnsafe)
        {
            differences |= PublicApiSymbolDifferences.Modifiers;
        }

        differences |= CompareAttributes(x.Attributes, y.Attributes);
        differences |= CompareAttributes(x.ReturnAttributes, y.ReturnAttributes);
        return differences;
    }

    private static PublicApiSymbolDifferences CompareMethods(PublicApiMethod x, PublicApiMethod y)
    {
        if (x.MethodKind != y.MethodKind)
            return PublicApiSymbolDifferences.Kind;

        var differences = CompareMemberModifiers(x, y);
        if (x.IsReadOnly != y.IsReadOnly || x.IsExtensionMethod != y.IsExtensionMethod)
        {
            differences |= PublicApiSymbolDifferences.Modifiers;
        }

        if (x.ReturnRefKind != y.ReturnRefKind)
        {
            differences |= PublicApiSymbolDifferences.Signature;
        }

        differences |= CompareTypeReference(x.ReturnType, y.ReturnType);
        differences |= CompareAttributes(x.ReturnAttributes, y.ReturnAttributes);
        differences |= CompareParameters(x.Parameters, y.Parameters);
        differences |= CompareGenericParameters(x.GenericParameters, y.GenericParameters);
        return differences;
    }

    private static PublicApiSymbolDifferences CompareMemberModifiers(PublicApiMember x, PublicApiMember y)
    {
        var differences = PublicApiSymbolDifferences.None;
        if (x.IsStatic != y.IsStatic ||
            x.IsAbstract != y.IsAbstract ||
            x.IsVirtual != y.IsVirtual ||
            x.IsOverride != y.IsOverride ||
            x.IsSealed != y.IsSealed ||
            x.IsNew != y.IsNew ||
            x.RequiresUnsafe != y.RequiresUnsafe)
        {
            differences |= PublicApiSymbolDifferences.Modifiers;
        }

        if (x.IsExplicitInterfaceImplementation != y.IsExplicitInterfaceImplementation ||
            !x.ExplicitInterfaceImplementations.Select(static member => member.DocumentationId).SequenceEqual(y.ExplicitInterfaceImplementations.Select(static member => member.DocumentationId), StringComparer.Ordinal))
        {
            differences |= PublicApiSymbolDifferences.Signature;
        }

        return differences;
    }

    private static PublicApiSymbolDifferences CompareParameters(ImmutableArray<PublicApiParameter> x, ImmutableArray<PublicApiParameter> y)
    {
        if (x.Length != y.Length)
            return PublicApiSymbolDifferences.Signature;

        var differences = PublicApiSymbolDifferences.None;
        for (var i = 0; i < x.Length; i++)
        {
            var left = x[i];
            var right = y[i];
            if (left.RefKind != right.RefKind ||
                left.IsParams != right.IsParams ||
                left.IsParamsCollection != right.IsParamsCollection ||
                left.IsScoped != right.IsScoped ||
                left.IsThis != right.IsThis ||
                left.IsOptional != right.IsOptional)
            {
                differences |= PublicApiSymbolDifferences.Signature;
            }

            if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal))
            {
                differences |= PublicApiSymbolDifferences.ParameterNames;
            }

            if (left.HasDefaultValue != right.HasDefaultValue || !Equals(left.DefaultValue, right.DefaultValue))
            {
                differences |= PublicApiSymbolDifferences.Value;
            }

            differences |= CompareTypeReference(left.Type, right.Type);
            differences |= CompareAttributes(left.Attributes, right.Attributes);
        }

        return differences;
    }

    private static PublicApiSymbolDifferences CompareGenericParameters(ImmutableArray<PublicApiGenericParameter> x, ImmutableArray<PublicApiGenericParameter> y)
    {
        if (x.Length != y.Length)
            return PublicApiSymbolDifferences.Signature;

        var differences = PublicApiSymbolDifferences.None;
        for (var i = 0; i < x.Length; i++)
        {
            var left = x[i];
            var right = y[i];
            if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal) || left.Variance != right.Variance)
            {
                differences |= PublicApiSymbolDifferences.Signature;
            }

            if (left.HasReferenceTypeConstraint != right.HasReferenceTypeConstraint ||
                left.HasValueTypeConstraint != right.HasValueTypeConstraint ||
                left.HasUnmanagedTypeConstraint != right.HasUnmanagedTypeConstraint ||
                left.HasConstructorConstraint != right.HasConstructorConstraint ||
                left.AllowsRefLikeType != right.AllowsRefLikeType)
            {
                differences |= PublicApiSymbolDifferences.Constraints;
            }

            // The nullable annotation of a generic parameter encodes the 'class?' and 'notnull' constraints
            if (left.NullableAnnotation != right.NullableAnnotation)
            {
                differences |= PublicApiSymbolDifferences.Constraints;
            }

            var constraintDifferences = CompareTypeReferenceSets(left.ConstraintTypes, right.ConstraintTypes);
            if (constraintDifferences.HasFlag(PublicApiSymbolDifferences.Signature))
            {
                differences |= PublicApiSymbolDifferences.Constraints;
            }

            differences |= constraintDifferences & PublicApiSymbolDifferences.Nullability;
            differences |= CompareAttributes(left.Attributes, right.Attributes);
        }

        return differences;
    }

    private static PublicApiSymbolDifferences CompareTypeReference(PublicApiTypeReference? x, PublicApiTypeReference? y)
    {
        if (PublicApiTypeReferenceComparer.Default.Equals(x, y))
            return PublicApiSymbolDifferences.None;

        return PublicApiTypeReferenceComparer.IgnoreNullability.Equals(x, y)
            ? PublicApiSymbolDifferences.Nullability
            : PublicApiSymbolDifferences.Signature;
    }

    private static PublicApiSymbolDifferences CompareTypeReferenceLists(ImmutableArray<PublicApiTypeReference> x, ImmutableArray<PublicApiTypeReference> y)
    {
        if (x.Length != y.Length)
            return PublicApiSymbolDifferences.Signature;

        var differences = PublicApiSymbolDifferences.None;
        for (var i = 0; i < x.Length; i++)
        {
            differences |= CompareTypeReference(x[i], y[i]);
        }

        return differences;
    }

    // The order of interfaces and constraints is not significant
    private static PublicApiSymbolDifferences CompareTypeReferenceSets(ImmutableArray<PublicApiTypeReference> x, ImmutableArray<PublicApiTypeReference> y)
    {
        if (IsSameMultiset(x, y, PublicApiTypeReferenceComparer.Default))
            return PublicApiSymbolDifferences.None;

        return IsSameMultiset(x, y, PublicApiTypeReferenceComparer.IgnoreNullability)
            ? PublicApiSymbolDifferences.Nullability
            : PublicApiSymbolDifferences.Signature;
    }

    private static PublicApiSymbolDifferences CompareAttributes(ImmutableArray<PublicApiAttribute> x, ImmutableArray<PublicApiAttribute> y)
    {
        var left = x.Where(static attribute => !IgnoredAttributes.Contains(attribute.AttributeType.FullName)).ToImmutableArray();
        var right = y.Where(static attribute => !IgnoredAttributes.Contains(attribute.AttributeType.FullName)).ToImmutableArray();
        return IsSameMultiset(left, right, AttributeComparer.Instance) ? PublicApiSymbolDifferences.None : PublicApiSymbolDifferences.Attributes;
    }

    private static bool IsSameMultiset<T>(ImmutableArray<T> x, ImmutableArray<T> y, IEqualityComparer<T> comparer)
        where T : notnull
    {
        if (x.Length != y.Length)
            return false;

        var counts = new Dictionary<T, int>(comparer);
        foreach (var item in x)
        {
            counts[item] = counts.GetValueOrDefault(item) + 1;
        }

        foreach (var item in y)
        {
            if (!counts.TryGetValue(item, out var count) || count == 0)
                return false;

            counts[item] = count - 1;
        }

        return true;
    }

    private sealed class AttributeComparer : IEqualityComparer<PublicApiAttribute>
    {
        public static AttributeComparer Instance { get; } = new();

        public bool Equals(PublicApiAttribute? x, PublicApiAttribute? y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x is null || y is null)
                return false;

            return PublicApiTypeReferenceComparer.Default.Equals(x.AttributeType, y.AttributeType) &&
                   x.AreArgumentsDecoded == y.AreArgumentsDecoded &&
                   x.ConstructorArguments.SequenceEqual(y.ConstructorArguments, ArgumentEquals) &&
                   x.NamedArguments.SequenceEqual(y.NamedArguments, static (left, right) =>
                       string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
                       left.Kind == right.Kind &&
                       ArgumentEquals(left.Value, right.Value));
        }

        public int GetHashCode(PublicApiAttribute obj) => PublicApiTypeReferenceComparer.Default.GetHashCode(obj.AttributeType);

        private static bool ArgumentEquals(PublicApiAttributeArgument x, PublicApiAttributeArgument y)
        {
            if (x.Kind != y.Kind || !PublicApiTypeReferenceComparer.IgnoreNullability.Equals(x.Type, y.Type))
                return false;

            return (x.Value, y.Value) switch
            {
                (null, null) => true,
                (ImmutableArray<PublicApiAttributeArgument> left, ImmutableArray<PublicApiAttributeArgument> right) => left.SequenceEqual(right, ArgumentEquals),
                (PublicApiTypeReference left, PublicApiTypeReference right) => PublicApiTypeReferenceComparer.IgnoreNullability.Equals(left, right),
                var (left, right) => Equals(left, right),
            };
        }
    }
}
