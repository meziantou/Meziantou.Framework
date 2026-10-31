using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A C# 14 extension block (e.g. <c>extension&lt;T&gt;(List&lt;T&gt; list) { ... }</c>) declared in a static class.</summary>
/// <remarks>
/// The compiler emits each extension member twice: an implementation method on the static class, which is listed in
/// <see cref="PublicApiType.Members"/>, and a member of an unspeakable grouping type that keeps the declaration as written
/// in source. <see cref="Members"/> contains the members as written in source. Use <see cref="PublicApiMethod.ExtensionImplementation"/>
/// and <see cref="PublicApiMethod.ImplementedExtensionMember"/> to go from one to the other.
/// </remarks>
public sealed class PublicApiExtensionBlock
{
    internal PublicApiExtensionBlock(
        string groupingTypeMetadataName,
        string markerTypeMetadataName,
        PublicApiParameter receiver,
        ImmutableArray<PublicApiGenericParameter> genericParameters,
        ImmutableArray<PublicApiMember> members)
    {
        GroupingTypeMetadataName = groupingTypeMetadataName;
        MarkerTypeMetadataName = markerTypeMetadataName;
        Receiver = receiver;
        GenericParameters = genericParameters;
        Members = members;
    }

    /// <summary>Gets the static class that declares the extension block.</summary>
    public PublicApiType DeclaringType { get; internal set; } = null!;

    /// <summary>
    /// Gets the metadata name of the compiler-generated grouping type (e.g. <c>&lt;G&gt;$8048A6C8BE30A622530249B904B537EB</c>).
    /// It is part of the documentation IDs of the <see cref="Members"/>. Blocks with the same receiver type share it.
    /// </summary>
    public string GroupingTypeMetadataName { get; }

    /// <summary>Gets the metadata name of the compiler-generated marker type (e.g. <c>&lt;M&gt;$C43E2675C7BBF9284AF22FB8A9BF0280</c>), which identifies the block in its grouping type.</summary>
    public string MarkerTypeMetadataName { get; }

    /// <summary>Gets the receiver parameter. Its <see cref="PublicApiParameter.Name"/> is <see langword="null"/> or empty when the block only declares static members (e.g. <c>extension(string)</c>).</summary>
    public PublicApiParameter Receiver { get; }

    /// <summary>Gets the type parameters of the block (e.g. <c>T</c> in <c>extension&lt;T&gt;(List&lt;T&gt; list)</c>).</summary>
    public ImmutableArray<PublicApiGenericParameter> GenericParameters { get; }

    /// <summary>Gets the members of the block, as written in source. Instance members have no receiver parameter and static members are <see cref="PublicApiMember.IsStatic"/>.</summary>
    public ImmutableArray<PublicApiMember> Members { get; }

    public override string ToString() => DeclaringType is null ? MarkerTypeMetadataName : DeclaringType.FullName + "." + GroupingTypeMetadataName + "." + MarkerTypeMetadataName;
}
