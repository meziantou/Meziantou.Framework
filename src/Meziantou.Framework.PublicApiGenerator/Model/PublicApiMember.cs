using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A field, a property, an event or a method.</summary>
public abstract class PublicApiMember : PublicApiSymbol
{
    private protected PublicApiMember(string name, PublicApiAccessibility accessibility, ImmutableArray<PublicApiAttribute> attributes, PublicApiMetadataOrigin? origin, PublicApiMemberModifiers modifiers, ImmutableArray<PublicApiMemberReference> explicitInterfaceImplementations)
        : base(name, accessibility, attributes, origin)
    {
        IsStatic = modifiers.IsStatic;
        IsAbstract = modifiers.IsAbstract;
        IsVirtual = modifiers.IsVirtual;
        IsOverride = modifiers.IsOverride;
        IsSealed = modifiers.IsSealed;
        IsNew = modifiers.IsNew;
        RequiresUnsafe = modifiers.RequiresUnsafe;
        IsExplicitInterfaceImplementation = modifiers.IsExplicitInterfaceImplementation;
        ExplicitInterfaceImplementations = explicitInterfaceImplementations.IsDefault ? [] : explicitInterfaceImplementations;
    }

    public bool IsStatic { get; }

    public bool IsAbstract { get; }

    /// <summary>Gets a value indicating whether the member is virtual and introduces a new slot (<c>virtual</c> in C#).</summary>
    public bool IsVirtual { get; }

    /// <summary>Gets a value indicating whether the member overrides an inherited member (<c>override</c> in C#).</summary>
    public bool IsOverride { get; }

    /// <summary>Gets a value indicating whether the member is a sealed override (<c>sealed override</c> in C#).</summary>
    public bool IsSealed { get; }

    /// <summary>
    /// Gets a value indicating whether the member hides a member inherited from a base type that is visible outside its assembly (<c>new</c> in C#).
    /// The modifier is not stored in metadata, so it is inferred from the members of the base types. The base types that cannot be found are ignored.
    /// </summary>
    public bool IsNew { get; }

    /// <summary>
    /// Gets a value indicating whether the compiler marked the member as requiring an unsafe context.
    /// It is only set for assemblies compiled with the updated memory safety rules (see <see cref="PublicApiAssembly.UsesUpdatedMemorySafetyRules"/>).
    /// </summary>
    public bool RequiresUnsafe { get; }

    /// <summary>Gets a value indicating whether the member is an explicit interface implementation (e.g. <c>void IDisposable.Dispose()</c>).</summary>
    public bool IsExplicitInterfaceImplementation { get; }

    /// <summary>Gets the interface members implemented explicitly by this member, as recorded in the <c>MethodImpl</c> metadata table.</summary>
    public ImmutableArray<PublicApiMemberReference> ExplicitInterfaceImplementations { get; }
}
