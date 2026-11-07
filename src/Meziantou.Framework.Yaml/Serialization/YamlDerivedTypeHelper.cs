namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Resolves derived types registered for polymorphic serialization. An open generic derived type is closed by
/// unifying the base type specification it declares with the closed base type currently being serialized.
/// </summary>
internal static class YamlDerivedTypeHelper
{
    /// <summary>
    /// Returns the closed derived type to register for <paramref name="baseType"/>.
    /// Types that are already closed are returned unchanged.
    /// </summary>
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2055",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    public static Type ResolveDerivedType(Type baseType, Type derivedType)
    {
        if (!derivedType.ContainsGenericParameters)
        {
            return derivedType;
        }

        if (!derivedType.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException($"Derived type '{derivedType}' cannot be resolved for base type '{baseType}' because it is a partially constructed generic type.");
        }

        Type? resolved = null;
        foreach (var candidate in EnumerateBaseTypes(derivedType))
        {
            var substitution = new Dictionary<Type, Type>();
            if (!TryUnify(candidate, baseType, substitution))
            {
                continue;
            }

            var parameters = derivedType.GetGenericArguments();
            var arguments = new Type[parameters.Length];
            var isComplete = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (!substitution.TryGetValue(parameters[i], out var argument))
                {
                    isComplete = false;
                    break;
                }

                arguments[i] = argument;
            }

            if (!isComplete)
            {
                continue;
            }

            Type constructed;
            try
            {
                constructed = derivedType.MakeGenericType(arguments);
            }
            catch (ArgumentException)
            {
                // The generic constraints of the derived type are not satisfied by the base type arguments.
                continue;
            }

            if (!baseType.IsAssignableFrom(constructed))
            {
                continue;
            }

            if (resolved is null)
            {
                resolved = constructed;
            }
            else if (resolved != constructed)
            {
                throw new InvalidOperationException($"Derived type '{derivedType}' is ambiguous for base type '{baseType}' because it can be constructed in more than one way.");
            }
        }

        return resolved ?? throw new InvalidOperationException($"Derived type '{derivedType}' cannot be resolved for base type '{baseType}'. Ensure the type arguments of the open generic derived type can be inferred from the base type.");
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "This code path is only used by reflection-based serialization. NativeAOT/trimming scenarios should use source-generated metadata.")]
    private static IEnumerable<Type> EnumerateBaseTypes(Type derivedType)
    {
        for (var current = derivedType.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }

        foreach (var interfaceType in derivedType.GetInterfaces())
        {
            yield return interfaceType;
        }
    }

    /// <summary>
    /// Matches a base type specification declared by an open generic derived type (such as <c>Base&lt;List&lt;T&gt;&gt;</c>)
    /// against the closed base type, recording the type arguments bound to each generic parameter.
    /// </summary>
    private static bool TryUnify(Type specification, Type actual, Dictionary<Type, Type> substitution)
    {
        if (specification.IsGenericParameter)
        {
            if (substitution.TryGetValue(specification, out var existing))
            {
                return existing == actual;
            }

            substitution[specification] = actual;
            return true;
        }

        if (specification.IsArray)
        {
            if (!actual.IsArray || specification.GetArrayRank() != actual.GetArrayRank())
            {
                return false;
            }

            // int[] and int[*] have the same rank but are distinct types.
            if (specification.IsSZArray != actual.IsSZArray)
            {
                return false;
            }

            return TryUnify(specification.GetElementType()!, actual.GetElementType()!, substitution);
        }

        if (specification.IsGenericType)
        {
            if (!actual.IsGenericType || specification.GetGenericTypeDefinition() != actual.GetGenericTypeDefinition())
            {
                return false;
            }

            var specificationArguments = specification.GetGenericArguments();
            var actualArguments = actual.GetGenericArguments();
            if (specificationArguments.Length != actualArguments.Length)
            {
                return false;
            }

            for (var i = 0; i < specificationArguments.Length; i++)
            {
                if (!TryUnify(specificationArguments[i], actualArguments[i], substitution))
                {
                    return false;
                }
            }

            return true;
        }

        return specification == actual;
    }
}
