using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A parameter of a method, an indexer or a delegate.</summary>
public sealed class PublicApiParameter
{
    internal PublicApiParameter(
        string? name,
        int ordinal,
        PublicApiTypeReference type,
        PublicApiRefKind refKind,
        ImmutableArray<PublicApiAttribute> attributes,
        bool isParams,
        bool isParamsCollection,
        bool isScoped,
        bool isThis,
        bool isOptional,
        bool hasDefaultValue,
        object? defaultValue,
        ImmutableArray<string> defaultValueEnumMemberNames,
        bool hasOutAttribute)
    {
        Name = name;
        Ordinal = ordinal;
        Type = type;
        RefKind = refKind;
        Attributes = attributes;
        IsParams = isParams;
        IsParamsCollection = isParamsCollection;
        IsScoped = isScoped;
        IsThis = isThis;
        IsOptional = isOptional;
        HasDefaultValue = hasDefaultValue;
        DefaultValue = defaultValue;
        DefaultValueEnumMemberNames = defaultValueEnumMemberNames;
        HasOutAttribute = hasOutAttribute;
    }

    /// <summary>Gets the name of the parameter, or <see langword="null"/> when metadata does not name it.</summary>
    public string? Name { get; }

    /// <summary>Gets the zero-based position of the parameter.</summary>
    public int Ordinal { get; }

    public PublicApiTypeReference Type { get; }

    public PublicApiRefKind RefKind { get; }

    public ImmutableArray<PublicApiAttribute> Attributes { get; }

    /// <summary>Gets a value indicating whether the parameter is a <c>params</c> array.</summary>
    public bool IsParams { get; }

    /// <summary>Gets a value indicating whether the parameter is a <c>params</c> collection other than an array (e.g. <c>params ReadOnlySpan&lt;T&gt;</c>).</summary>
    public bool IsParamsCollection { get; }

    public bool IsScoped { get; }

    /// <summary>Gets a value indicating whether the parameter is the receiver of an extension method.</summary>
    public bool IsThis { get; }

    public bool IsOptional { get; }

    public bool HasDefaultValue { get; }

    /// <summary>
    /// Gets the default value, as stored in metadata. A <c>default</c> struct value is stored as <see langword="null"/>.
    /// A <see cref="decimal"/> value is read from the <c>DecimalConstantAttribute</c> of the parameter.
    /// </summary>
    public object? DefaultValue { get; }

    // The names of the enum members that make the default value, when the parameter is of an enum type and the value can be named
    internal ImmutableArray<string> DefaultValueEnumMemberNames { get; }

    // The [Out] pseudo-attribute, which is also set on parameters that are not passed by reference (e.g. arrays in interop signatures)
    internal bool HasOutAttribute { get; }

    public override string ToString() => Name ?? string.Empty;
}
