namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Options for <see cref="PublicApiFormatter"/>.</summary>
public sealed class PublicApiFormattingOptions
{
    public PublicApiDeclarationStyle Style { get; set; } = PublicApiDeclarationStyle.Declaration;

    /// <summary>Gets or sets a value indicating whether attributes are written. The default value is <see langword="true"/>.</summary>
    public bool IncludeAttributes { get; set; } = true;

    /// <summary>
    /// Gets or sets a predicate that selects the attributes to write. When it is <see langword="null"/>, the attributes of the <c>System</c> namespaces
    /// are written, except the ones that only matter to the compiler or that are represented by the C# syntax (e.g. <c>NullableAttribute</c>, <c>ExtensionAttribute</c> or <c>EditorBrowsableAttribute</c>).
    /// </summary>
    public Func<PublicApiAttribute, bool>? AttributeFilter { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether type names include their namespace in the <see cref="PublicApiDeclarationStyle.Declaration"/> style.
    /// The default value is <see langword="true"/>. Segments reference the types in both cases.
    /// </summary>
    public bool QualifyTypeNames { get; set; } = true;

    /// <summary>Gets or sets the string used to separate lines. The default value is <see cref="Environment.NewLine"/>.</summary>
    public string NewLine { get; set; } = Environment.NewLine;
}
