using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.Yaml.SourceGeneration;

/// <summary>
/// Describes a <c>closed</c> type hierarchy from the symbol model so derived type registrations can be inferred.
/// </summary>
/// <remarks>
/// <c>ITypeSymbol.IsClosed</c> is unavailable in the Roslyn reference assemblies the generator compiles against, so
/// it is used through reflection when the compiler running the generator provides it. Older compilers do not
/// understand the <c>closed</c> modifier at all, so the fallback only has to recognize source declarations, which it
/// does by looking for the modifier. The derived types are always reconstructed from the symbol model: the
/// <c>closed</c> modifier restricts subtyping to the module declaring the base type, so the direct derived types are
/// exactly the named types of that module whose base type shares the closed type's original definition.
/// </remarks>
internal static class ClosedTypeSymbolHelper
{
    private static readonly Func<ITypeSymbol, bool>? IsClosedAccessor = CreateIsClosedAccessor();

    public static bool IsClosedType(INamedTypeSymbol type)
    {
        if (type is not { TypeKind: TypeKind.Class, IsAbstract: true })
        {
            return false;
        }

        if (IsClosedAccessor is not null)
        {
            return IsClosedAccessor(type);
        }

        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not BaseTypeDeclarationSyntax declaration)
            {
                continue;
            }

            foreach (var modifier in declaration.Modifiers)
            {
                // The 'closed' contextual keyword has a dedicated SyntaxKind on compilers that understand it, but that
                // enum member does not exist in the Roslyn version the generator compiles against. The token text is
                // stable across compiler versions.
                if (string.Equals(modifier.Text, "closed", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the direct derived types of a closed type. Generic derived types are returned in unbound form so callers
    /// can unify them against the constructed base type.
    /// </summary>
    public static ImmutableArray<ITypeSymbol> GetClosedDerivedTypes(INamedTypeSymbol closedType)
    {
        var baseDefinition = closedType.OriginalDefinition;
        var derivedTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();

        foreach (var candidate in EnumerateNamedTypes(closedType.ContainingModule.GlobalNamespace))
        {
            if (candidate.BaseType is { } candidateBase &&
                SymbolEqualityComparer.Default.Equals(candidateBase.OriginalDefinition, baseDefinition))
            {
                derivedTypes.Add(candidate.IsGenericType ? candidate.ConstructUnboundGenericType() : candidate);
            }
        }

        return derivedTypes.ToImmutable();
    }

    /// <summary>Gets the discriminator synthesized for an inferred derived type: its name without the generic arity suffix.</summary>
    public static string GetInferredDiscriminator(ITypeSymbol type) => type.Name;

    /// <summary>
    /// Determines whether <paramref name="type"/> is at least as visible as <paramref name="other"/>, comparing the
    /// effective visibility of each type, that is the most restrictive accessibility found on the type itself and on
    /// every type it is nested in. An inferred derived type that is less visible than its base type cannot be
    /// referenced from every place the base type can, so the generated context could not reference it.
    /// </summary>
    public static bool IsAtLeastAsVisibleAs(ITypeSymbol type, ITypeSymbol other)
        => GetEffectiveVisibility(type) >= GetEffectiveVisibility(other);

    private static Func<ITypeSymbol, bool>? CreateIsClosedAccessor()
    {
        var getter = typeof(ITypeSymbol).GetProperty("IsClosed")?.GetMethod;
        return getter is null ? null : (Func<ITypeSymbol, bool>)getter.CreateDelegate(typeof(Func<ITypeSymbol, bool>));
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
            {
                foreach (var namedType in EnumerateNamedTypes(childNamespace))
                {
                    yield return namedType;
                }
            }
            else if (member is INamedTypeSymbol namedType)
            {
                yield return namedType;

                foreach (var nestedType in EnumerateNestedTypes(namedType))
                {
                    yield return nestedType;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nestedType in type.GetTypeMembers())
        {
            yield return nestedType;

            foreach (var deeperType in EnumerateNestedTypes(nestedType))
            {
                yield return deeperType;
            }
        }
    }

    private static Accessibility GetEffectiveVisibility(ITypeSymbol type)
    {
        var visibility = Accessibility.Public;
        for (ISymbol? current = type; current is INamedTypeSymbol; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility < visibility)
            {
                visibility = current.DeclaredAccessibility;
            }
        }

        return visibility;
    }
}
