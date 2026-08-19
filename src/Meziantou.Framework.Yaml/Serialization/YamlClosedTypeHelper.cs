using System.Reflection;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Reads the compiler-emitted metadata describing a <c>closed</c> type hierarchy so derived type
/// registrations can be inferred instead of being duplicated with <see cref="YamlDerivedTypeAttribute"/>.
/// </summary>
internal static class YamlClosedTypeHelper
{
    private const string IsClosedTypeAttributeFullName = "System.Runtime.CompilerServices.IsClosedTypeAttribute";

    /// <summary>
    /// Determines whether <paramref name="type"/> is declared <c>closed</c>, reporting the derived types the
    /// compiler recorded for it in <paramref name="derivedTypes"/>.
    /// </summary>
    /// <remarks>
    /// The two results are independent: a closed type declaring no derived type is still a closed type, so
    /// <paramref name="derivedTypes"/> is <see langword="null"/> both for a type that is not closed and for a
    /// closed type with an empty hierarchy. The marker attribute is matched by full name because the compiler
    /// emits its own copy into assemblies targeting a runtime that does not provide the type.
    /// </remarks>
    public static bool IsClosedType(Type type, out Type[]? derivedTypes)
    {
        derivedTypes = null;

        foreach (var attributeData in type.GetCustomAttributesData())
        {
            var attributeType = attributeData.AttributeType;
            if (!string.Equals(attributeType.Name, "IsClosedTypeAttribute", StringComparison.Ordinal) ||
                !string.Equals(attributeType.FullName, IsClosedTypeAttributeFullName, StringComparison.Ordinal))
            {
                continue;
            }

            derivedTypes = GetDeclaredDerivedTypes(attributeData);
            return true;
        }

        return false;
    }

    /// <summary>Gets the discriminator synthesized for an inferred derived type: its name without the generic arity suffix.</summary>
    public static string GetInferredDiscriminator(Type type)
    {
        var name = type.Name;
        var arityIndex = name.IndexOf('`', StringComparison.Ordinal);
        return arityIndex < 0 ? name : name[..arityIndex];
    }

    /// <summary>
    /// Determines whether <paramref name="type"/> is at least as visible as <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// An inferred derived type must be at least as visible as the base type it is registered under; otherwise
    /// there are call sites that can reference the base but not the derived type, and the source generator could
    /// not emit a reference to it. The comparison uses the effective visibility of each type, that is the most
    /// restrictive accessibility found on the type itself and on every type it is nested in.
    /// </remarks>
    public static bool IsAtLeastAsVisibleAs(Type type, Type other)
        => GetEffectiveVisibility(type) >= GetEffectiveVisibility(other);

    /// <summary>
    /// Reads the derived type list from the compiler-emitted closed type marker. Returns <see langword="null"/>
    /// when the hierarchy is empty or the attribute cannot be interpreted.
    /// </summary>
    private static Type[]? GetDeclaredDerivedTypes(CustomAttributeData attributeData)
    {
        foreach (var namedArgument in attributeData.NamedArguments)
        {
            if (!string.Equals(namedArgument.MemberName, "DerivedTypes", StringComparison.Ordinal))
            {
                continue;
            }

            // Mono materializes array-valued named arguments directly, while CoreCLR wraps each element in a CustomAttributeTypedArgument.
            if (namedArgument.TypedValue.Value is Type[] materializedDerivedTypes)
            {
                return Array.Exists(materializedDerivedTypes, derivedType => derivedType is null) ? null : materializedDerivedTypes;
            }

            if (namedArgument.TypedValue.Value is not IList<CustomAttributeTypedArgument> derivedTypeArguments)
            {
                return null;
            }

            var derivedTypes = new Type[derivedTypeArguments.Count];
            for (var i = 0; i < derivedTypes.Length; i++)
            {
                if (derivedTypeArguments[i].Value is not Type derivedType)
                {
                    return null;
                }

                derivedTypes[i] = derivedType;
            }

            return derivedTypes;
        }

        // The closed type marker is present but carries no derived type.
        return null;
    }

    private static Visibility GetEffectiveVisibility(Type type)
    {
        var visibility = Visibility.Public;
        for (Type? current = type; current is not null; current = current.DeclaringType)
        {
            var declared = GetDeclaredVisibility(current);
            if (declared < visibility)
            {
                visibility = declared;
            }
        }

        return visibility;
    }

    private static Visibility GetDeclaredVisibility(Type type)
    {
        if (type.IsPublic || type.IsNestedPublic)
        {
            return Visibility.Public;
        }

        if (type.IsNestedFamORAssem)
        {
            return Visibility.ProtectedOrInternal;
        }

        if (type.IsNestedFamily)
        {
            return Visibility.Protected;
        }

        if (type.IsNestedFamANDAssem)
        {
            return Visibility.ProtectedAndInternal;
        }

        if (type.IsNestedPrivate)
        {
            return Visibility.Private;
        }

        // Top-level non-public types and nested assembly types are both internal.
        return Visibility.Internal;
    }

    private enum Visibility
    {
        Private,
        ProtectedAndInternal,
        Protected,
        Internal,
        ProtectedOrInternal,
        Public,
    }
}
