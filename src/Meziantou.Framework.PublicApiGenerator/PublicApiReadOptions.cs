namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Options for <see cref="PublicApi.ReadAssembly(string, PublicApiReadOptions?)"/>.</summary>
public sealed class PublicApiReadOptions
{
    /// <summary>
    /// Gets or sets the target framework of the assembly (e.g. <c>net8.0</c> or <c>.NETCoreApp,Version=v8.0</c>). The value is kept as provided.
    /// When it is not set, it is inferred from the <c>TargetFrameworkAttribute</c> of the assembly.
    /// </summary>
    public string? TargetFramework { get; set; }

    /// <summary>
    /// Gets or sets an identity for the input, such as a package identity and the path of the assembly in the package (e.g. <c>MyPackage/1.0.0/lib/net8.0/MyLibrary.dll</c>).
    /// When set, it is used as the scope of the symbol identities (see <see cref="PublicApiAssembly.Scope"/>). Avoid machine-specific values such as absolute file paths.
    /// </summary>
    public string? InputIdentity { get; set; }
}
