namespace Meziantou.Framework.NuGetPackageValidation.Rules;

internal static class ErrorCodes
{
    public const int FileNotFound = 1;

    public const int AuthorNotSet = 11;
    public const int DefaultAuthorSet = 12;

    public const int LicenseNotSet = 21;
    public const int UseDeprecatedLicenseUrl = 22;
    public const int LicenseFileNotFound = 23;

    public const int UseDeprecatedIconUrl = 31;
    public const int IconNotSet = 32;
    public const int IconNotFound = 33;
    public const int IconFileTooLarge = 34;
    public const int IconFileFormatNotSupported = 35;
    public const int IconFileInvalidExtension = 36;

    public const int UseDeprecatedSummary = 41;
    public const int DescriptionNotSet = 42;
    public const int PackageHasDefaultDescription = 43;
    public const int PackageDescriptionIsTooLong = 44;

    public const int ProjectUrlNotSet = 51;

    public const int ReadmeNotSet = 61;
    public const int ReadmeFileNotFound = 62;

    public const int RepositoryNotSet = 71;
    public const int RepositoryTypeNotSet = 72;
    public const int RepositoryUrlNotSet = 73;
    public const int RepositoryCommitNotSet = 74;
    public const int RepositoryBranchNotSet = 75;

    public const int AssemblyIsNotOptimized = 81;

    public const int CannotCheckPackageIdExistsOnNuGetOrg = 91;
    public const int PackageIdExistsOnNuGetOrg = 92;

    public const int XmlDocumentationNotFound = 101;

    public const int SymbolsNotFound = 111;
    public const int NonDeterministic = 112;
    public const int SourceFileNotAccessible = 113;
    public const int CompilerFlagsNotPresent = 114;
    public const int InvalidCompilerVersion = 115;
    public const int CompilerDoesNotSupportReproducibleBuilds = 116;
    public const int FullPdb = 117;
    public const int PdbDoesNotMatchAssembly = 118;
    public const int UrlIsNotAccessible = 119;

    public const int TagsNotSet = 131;
    public const int TagsTooLong = 132;
}
