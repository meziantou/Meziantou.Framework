using System.Text;
using Meziantou.Framework.Assertions.Architecture.Reading;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>A type used by the code: in a signature, an attribute, or an instruction. The type may be defined outside the architecture.</summary>
public sealed class TypeReference
{
    private static readonly IReadOnlyList<TypeReference> EmptyTypes = [];

    private readonly ArchitectureModel? _architecture;
    private readonly Lazy<TypeElement?>? _definition;

    private TypeReference(ArchitectureModel? architecture, TypeReferenceKind kind, string metadataName, string @namespace, string fullName, string? assemblyName)
    {
        _architecture = architecture;
        Kind = kind;
        MetadataName = metadataName;
        Name = MetadataNames.RemoveArity(metadataName);
        Namespace = @namespace;
        FullName = fullName;
        AssemblyName = assemblyName;
        if (kind == TypeReferenceKind.Named && architecture is not null)
        {
            _definition = new Lazy<TypeElement?>(() => architecture.FindType(this));
        }
    }

    /// <summary>Gets the shape of the type.</summary>
    public TypeReferenceKind Kind { get; }

    /// <summary>Gets the name of the type, without the namespace, the containing type and the generic arity (<c>List</c>).</summary>
    public string Name { get; }

    /// <summary>Gets the name of the type as written in metadata, with the generic arity (<c>List`1</c>).</summary>
    public string MetadataName { get; }

    /// <summary>Gets the namespace of the type, or of its outermost containing type for a nested type. It is empty for a type that is not named.</summary>
    public string Namespace { get; }

    /// <summary>Gets the full name of a named type as written in metadata (<c>System.Collections.Generic.List`1</c>, <c>MyApp.Outer+Inner</c>), without its generic arguments. For other kinds, it is the same as <see cref="ToString"/>.</summary>
    public string FullName { get; }

    /// <summary>Gets the simple name of the assembly the type is referenced from, or <see langword="null"/> when it is unknown. Type forwarders are not followed: a type of the framework is often referenced from <c>System.Runtime</c>.</summary>
    public string? AssemblyName { get; }

    /// <summary>Gets the containing type of a nested type.</summary>
    public TypeReference? DeclaringType { get; private init; }

    /// <summary>Gets the generic arguments of a generic instantiation (<c>List&lt;Order&gt;</c>), or an empty list.</summary>
    public IReadOnlyList<TypeReference> GenericArguments { get; private init; } = EmptyTypes;

    /// <summary>Gets the generic type definition of a generic instantiation (<c>List&lt;T&gt;</c> for <c>List&lt;Order&gt;</c>), or <see langword="null"/>.</summary>
    public TypeReference? GenericTypeDefinition { get; private init; }

    /// <summary>Gets the element type of an array, a pointer or a reference.</summary>
    public TypeReference? ElementType { get; private init; }

    /// <summary>Gets the rank of an array, or 0.</summary>
    public int ArrayRank { get; private init; }

    /// <summary>Gets the types of the signature of a function pointer (the return type first), or an empty list.</summary>
    public IReadOnlyList<TypeReference> SignatureTypes { get; private init; } = EmptyTypes;

    /// <summary>Gets a value indicating whether the type is a generic instantiation (<c>List&lt;Order&gt;</c>).</summary>
    public bool IsGenericInstance => GenericArguments.Count > 0;

    /// <summary>Gets the definition of a named type when one of the assemblies of the architecture defines it, or <see langword="null"/>.</summary>
    public TypeElement? Definition => GenericTypeDefinition is { } definition ? definition.Definition : _definition?.Value;

    /// <summary>Returns the type as written in C#: <c>System.Collections.Generic.List&lt;MyApp.Order&gt;</c>, <c>System.Collections.Generic.List&lt;&gt;</c>.</summary>
    /// <returns>The type as written in C#.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        AppendDisplayName(sb);
        return sb.ToString();
    }

    /// <summary>Determines whether the type is the named type <paramref name="type"/>, or a generic instantiation of it.</summary>
    /// <param name="type">The named type, or a generic type definition (<c>typeof(List&lt;&gt;)</c>).</param>
    /// <returns><see langword="true"/> when the full names match.</returns>
    public bool Is(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Kind == TypeReferenceKind.Named && string.Equals(FullName, MetadataNames.GetFullName(type), StringComparison.Ordinal);
    }

    /// <summary>Determines whether the type is the named type <typeparamref name="T"/>, or a generic instantiation of it.</summary>
    /// <typeparam name="T">The named type.</typeparam>
    /// <returns><see langword="true"/> when the full names match.</returns>
    public bool Is<T>() => Is(typeof(T));

    /// <summary>Determines whether the type is in the namespace <paramref name="namespace"/>, not in one of its child namespaces.</summary>
    /// <param name="namespace">The namespace (<c>MyApp.Domain</c>).</param>
    /// <returns><see langword="true"/> when <see cref="Namespace"/> is <paramref name="namespace"/>.</returns>
    /// <seealso cref="IsInNamespaceOrChildren"/>
    public bool IsInNamespace(string @namespace)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        return string.Equals(Namespace, @namespace, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether the type is in the namespace <paramref name="namespace"/> or in one of its child namespaces:
    /// <c>MyApp.Domain</c> matches <c>MyApp.Domain</c> and <c>MyApp.Domain.Events</c>, not <c>MyApp.DomainEvents</c>.
    /// </summary>
    /// <param name="namespace">The namespace (<c>MyApp.Domain</c>).</param>
    /// <returns><see langword="true"/> when <see cref="Namespace"/> is <paramref name="namespace"/> or starts with <paramref name="namespace"/> followed by a dot.</returns>
    /// <seealso cref="IsInNamespace"/>
    public bool IsInNamespaceOrChildren(string @namespace)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        return MetadataNames.IsInNamespaceOrChildren(Namespace, @namespace);
    }

    internal static TypeReference CreateNamed(ArchitectureModel? architecture, string @namespace, string metadataName, string? assemblyName, TypeReference? declaringType)
    {
        var fullName = declaringType is not null
            ? declaringType.FullName + "+" + metadataName
            : @namespace.Length == 0 ? metadataName : @namespace + "." + metadataName;

        return new TypeReference(architecture, TypeReferenceKind.Named, metadataName, declaringType?.Namespace ?? @namespace, fullName, assemblyName)
        {
            DeclaringType = declaringType,
        };
    }

    internal static TypeReference CreateGenericInstance(TypeReference definition, IReadOnlyList<TypeReference> arguments)
    {
        var definitionReference = definition.GenericTypeDefinition ?? definition;
        return new TypeReference(definitionReference._architecture, TypeReferenceKind.Named, definitionReference.MetadataName, definitionReference.Namespace, definitionReference.FullName, definitionReference.AssemblyName)
        {
            DeclaringType = definitionReference.DeclaringType,
            GenericTypeDefinition = definitionReference,
            GenericArguments = arguments,
        };
    }

    internal static TypeReference CreateArray(TypeReference elementType, int rank)
    {
        var name = elementType.ToString() + (rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]");
        return new TypeReference(architecture: null, TypeReferenceKind.Array, name, string.Empty, name, assemblyName: null) { ElementType = elementType, ArrayRank = rank };
    }

    internal static TypeReference CreatePointer(TypeReference elementType)
    {
        var name = elementType.ToString() + "*";
        return new TypeReference(architecture: null, TypeReferenceKind.UnmanagedPointer, name, string.Empty, name, assemblyName: null) { ElementType = elementType };
    }

    internal static TypeReference CreateByReference(TypeReference elementType)
    {
        var name = "ref " + elementType.ToString();
        return new TypeReference(architecture: null, TypeReferenceKind.ByReference, name, string.Empty, name, assemblyName: null) { ElementType = elementType };
    }

    internal static TypeReference CreateGenericParameter(string name)
    {
        return new TypeReference(architecture: null, TypeReferenceKind.GenericParameter, name, string.Empty, name, assemblyName: null);
    }

    internal static TypeReference CreateFunctionPointer(IReadOnlyList<TypeReference> signatureTypes)
    {
        var name = "delegate*<" + string.Join(", ", signatureTypes.Skip(1).Append(signatureTypes[0])) + ">";
        return new TypeReference(architecture: null, TypeReferenceKind.FunctionPointer, name, string.Empty, name, assemblyName: null) { SignatureTypes = signatureTypes };
    }

    // The named types the type is made of: the generic type definition, the generic arguments, the element type...
    internal void CollectNamedTypes(List<TypeReference> result)
    {
        switch (Kind)
        {
            case TypeReferenceKind.Named:
                result.Add(GenericTypeDefinition ?? this);
                foreach (var argument in GenericArguments)
                {
                    argument.CollectNamedTypes(result);
                }

                break;

            case TypeReferenceKind.Array or TypeReferenceKind.UnmanagedPointer or TypeReferenceKind.ByReference:
                ElementType!.CollectNamedTypes(result);
                break;

            case TypeReferenceKind.FunctionPointer:
                foreach (var type in SignatureTypes)
                {
                    type.CollectNamedTypes(result);
                }

                break;
        }
    }

    private void AppendDisplayName(StringBuilder sb)
    {
        if (Kind != TypeReferenceKind.Named)
        {
            sb.Append(FullName);
            return;
        }

        if (DeclaringType is not null)
        {
            DeclaringType.AppendDisplayName(sb);
            sb.Append('.');
        }
        else if (Namespace.Length > 0)
        {
            sb.Append(Namespace).Append('.');
        }

        sb.Append(Name);
        if (GenericArguments.Count > 0)
        {
            // The generic arguments of a nested type include the ones of its containing types
            var arity = MetadataNames.GetArity(MetadataName);
            var arguments = GenericArguments.Skip(Math.Max(0, GenericArguments.Count - arity)).ToList();
            if (arguments.Count > 0)
            {
                sb.Append('<');
                for (var i = 0; i < arguments.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    arguments[i].AppendDisplayName(sb);
                }

                sb.Append('>');
            }
        }
        else if (MetadataNames.GetArity(MetadataName) is > 0 and var arity)
        {
            sb.Append('<').Append(',', arity - 1).Append('>');
        }
    }
}
