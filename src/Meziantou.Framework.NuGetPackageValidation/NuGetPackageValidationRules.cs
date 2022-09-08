using System.Collections.Immutable;
using Meziantou.Framework.NuGetPackageValidation.Rules;

namespace Meziantou.Framework.NuGetPackageValidation;

public static class NuGetPackageValidationRules
{
    public static NuGetPackageValidationRule AssembliesMustBeOptimized { get; } = new AssembliesMustBeOptimizedMustBeSetValidationRule();
    public static NuGetPackageValidationRule AuthorMustBeSet { get; } = new AuthorMustBeSetValidationRule();
    public static NuGetPackageValidationRule DescriptionMustBeSet { get; } = new DescriptionMustBeSetValidationRule();
    public static NuGetPackageValidationRule IconMustBeSet { get; } = new IconMustBeSetValidationRule();
    public static NuGetPackageValidationRule LicenseMustBeSet { get; } = new LicenseMustBeSetValidationRule();
    public static NuGetPackageValidationRule PackageIdAvailableOnNuGetOrg { get; } = new PackageIdAvailableOnNuGetOrgValidationRule();
    public static NuGetPackageValidationRule ProjectUrlMustBeSet { get; } = new ProjectUrlBeSetValidationRule();
    public static NuGetPackageValidationRule ReadmeMustBeSet { get; } = new ReadmeMustBeSetValidationRule();
    public static NuGetPackageValidationRule RepositoryMustBeSet { get; } = new RepositoryInfoMustBeSetValidationRule();
    public static NuGetPackageValidationRule RepositoryBranchMustBeSet { get; } = new RepositoryBranchMustBeSetValidationRule();
    public static NuGetPackageValidationRule Symbols { get; } = new SymbolsValidationRule();
    public static NuGetPackageValidationRule TagsMustBeSet { get; } = new TagsMustBeSetValidationRule();
    public static NuGetPackageValidationRule XmlDocumentationMustBePresent { get; } = new XmlDocumentationMustBePresentValidationRule();

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
            RepositoryBranchMustBeSet,
            Symbols,
            TagsMustBeSet,
            XmlDocumentationMustBePresent);
    }
}
