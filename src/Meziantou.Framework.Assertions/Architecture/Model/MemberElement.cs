using System.Reflection;

namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>A member of a type: a method, a constructor, a property, a field or an event.</summary>
public abstract class MemberElement
{
    private readonly Lazy<IReadOnlyList<Dependency>> _dependencies;

    private protected MemberElement(TypeElement declaringType, string name)
    {
        DeclaringType = declaringType;
        Name = name;
        _dependencies = new(() => [.. DeclaringType.Dependencies.Where(dependency => dependency.SourceMember == this)]);
    }

    /// <summary>Gets the type that declares the member.</summary>
    public TypeElement DeclaringType { get; }

    /// <summary>Gets the name of the member as written in metadata (<c>.ctor</c> for a constructor).</summary>
    public string Name { get; }

    /// <summary>Gets the declared accessibility of the member.</summary>
    public abstract ElementAccessibility Accessibility { get; }

    /// <summary>Gets a value indicating whether the member is static.</summary>
    public abstract bool IsStatic { get; }

    /// <summary>Gets the attributes applied to the member.</summary>
    public abstract IReadOnlyList<AttributeElement> Attributes { get; }

    /// <summary>Gets the dependencies of the declaration and the code of the member, including the code of its lambdas and local functions.</summary>
    public IReadOnlyList<Dependency> Dependencies => _dependencies.Value;

    /// <summary>Gets the first statement of the member when the PDB is available, or <see langword="null"/>.</summary>
    public abstract SourceLocation? Location { get; }

    /// <summary>Determines whether the member has an attribute of the given type.</summary>
    /// <param name="attributeType">The type of the attribute.</param>
    /// <returns><see langword="true"/> when one of the attributes of the member is of this type.</returns>
    public bool HasAttribute(Type attributeType)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        return Attributes.Any(attribute => attribute.Type.Is(attributeType));
    }

    /// <summary>Returns the member as <c>DeclaringType.Name</c>.</summary>
    /// <returns>The member as <c>DeclaringType.Name</c>.</returns>
    public override string ToString() => DeclaringType + "." + Name;

    private protected static ElementAccessibility GetAccessibility(MethodAttributes attributes)
    {
        return (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => ElementAccessibility.Public,
            MethodAttributes.FamORAssem => ElementAccessibility.ProtectedInternal,
            MethodAttributes.Family => ElementAccessibility.Protected,
            MethodAttributes.Assembly => ElementAccessibility.Internal,
            MethodAttributes.FamANDAssem => ElementAccessibility.PrivateProtected,
            _ => ElementAccessibility.Private,
        };
    }

    private protected static ElementAccessibility GetAccessibility(FieldAttributes attributes)
    {
        return (attributes & FieldAttributes.FieldAccessMask) switch
        {
            FieldAttributes.Public => ElementAccessibility.Public,
            FieldAttributes.FamORAssem => ElementAccessibility.ProtectedInternal,
            FieldAttributes.Family => ElementAccessibility.Protected,
            FieldAttributes.Assembly => ElementAccessibility.Internal,
            FieldAttributes.FamANDAssem => ElementAccessibility.PrivateProtected,
            _ => ElementAccessibility.Private,
        };
    }
}
