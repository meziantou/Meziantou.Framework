using System.Collections.Immutable;
using Meziantou.Framework.NuGetPackageValidation.Rules;

namespace Meziantou.Framework.NuGetPackageValidation;

/// <summary>Provides predefined validation rules for NuGet packages.</summary>
public static class NuGetPackageValidationRules
{
    /// <summary>Gets a validation rule that ensures assemblies are compiled with optimizations enabled (Release configuration).</summary>
    public static NuGetPackageValidationRule AssembliesMustBeOptimized { get; } = new AssembliesMustBeOptimizedMustBeSetValidationRule();

    /// <summary>Gets a validation rule that ensures the package author metadata is set and not using default values.</summary>
    public static NuGetPackageValidationRule AuthorMustBeSet { get; } = new AuthorMustBeSetValidationRule();

    /// <summary>Gets a validation rule that ensures the package has a meaningful description.</summary>
    public static NuGetPackageValidationRule DescriptionMustBeSet { get; } = new DescriptionMustBeSetValidationRule();

    /// <summary>Gets a validation rule that ensures the package includes an icon file.</summary>
    public static NuGetPackageValidationRule IconMustBeSet { get; } = new IconMustBeSetValidationRule();

    /// <summary>Gets a validation rule that ensures the package includes license information using a license expression or file.</summary>
    public static NuGetPackageValidationRule LicenseMustBeSet { get; } = new LicenseMustBeSetValidationRule();

    /// <summary>Gets a validation rule that checks if the package ID is already taken on nuget.org.</summary>
    public static NuGetPackageValidationRule PackageIdAvailableOnNuGetOrg { get; } = new PackageIdAvailableOnNuGetOrgValidationRule();

    /// <summary>Gets a validation rule that ensures the package has a project URL specified and that it is accessible.</summary>
    public static NuGetPackageValidationRule ProjectUrlMustBeSet { get; } = new ProjectUrlBeSetValidationRule();

    /// <summary>Gets a validation rule that ensures the package includes a readme file.</summary>
    public static NuGetPackageValidationRule ReadmeMustBeSet { get; } = new ReadmeMustBeSetValidationRule();

    /// <summary>Gets a validation rule that ensures repository metadata is set including type, URL, and commit information.</summary>
    public static NuGetPackageValidationRule RepositoryMustBeSet { get; } = new RepositoryInfoMustBeSetValidationRule();

    /// <summary>Gets a validation rule that ensures the repository branch is specified in the package metadata.</summary>
    public static NuGetPackageValidationRule RepositoryBranchMustBeSet { get; } = new RepositoryBranchMustBeSetValidationRule();

    /// <summary>Gets a validation rule that performs comprehensive validation of debug symbols including checking for portable PDB format, deterministic builds, source link, and symbol availability.</summary>
    public static NuGetPackageValidationRule Symbols { get; } = new SymbolsValidationRule();

    /// <summary>Gets a validation rule that ensures the package has tags defined.</summary>
    public static NuGetPackageValidationRule TagsMustBeSet { get; } = new TagsMustBeSetValidationRule();

    /// <summary>Gets a validation rule that ensures XML documentation files are included for assemblies in the package.</summary>
    public static NuGetPackageValidationRule XmlDocumentationMustBePresent { get; } = new XmlDocumentationMustBePresentValidationRule();

    /// <summary>Gets the default set of validation rules recommended for most packages.</summary>
    public static ImmutableArray<NuGetPackageValidationRule> Default { get; } = CreateRuleSet();

    private static ImmutableArray<NuGetPackageValidationRule> CreateRuleSet()
    {
        return ImmutableArray.Create(
            AssembliesMustBeOptimized,
            AuthorMustBeSet,
            DescriptionMustBeSet,
            IconMustBeSet,
            LicenseMustBeSet,
            ProjectUrlMustBeSet,
            ReadmeMustBeSet,
            RepositoryMustBeSet,
            Symbols,
            TagsMustBeSet,
            XmlDocumentationMustBePresent);
    }
}
