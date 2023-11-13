using FluentAssertions;
using Meziantou.Framework.NuGetPackageValidation.Rules;
using Xunit;

namespace Meziantou.Framework.NuGetPackageValidation.Tests;

public sealed class NuGetPackageValidatorTests
{
    private static Task<NuGetPackageValidationResult> ValidateAsync(string packageName, int[] excludedRuleIds, params NuGetPackageValidationRule[] rules)
    {
        var path = FullPath.FromPath(typeof(NuGetPackageValidatorTests).Assembly.Location).Parent / "Packages" / packageName;
        return ValidateAsync(path, excludedRuleIds, rules);
    }

    private static async Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, int[] excludedRuleIds, params NuGetPackageValidationRule[] rules)
    {
        var options = new NuGetPackageValidationOptions();
        options.Rules.AddRange(rules);

        if (excludedRuleIds is not null)
        {
            options.ExcludedRuleIds.AddRange(excludedRuleIds);
        }

        return await NuGetPackageValidator.ValidateAsync(packagePath, options);
    }

    private static async Task<FullPath> DownloadPackageAsync(string packageName, string version)
    {
        var filePath = FullPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meziantou.FrameworkTests", "nuget", $"{packageName}.{version}.nupkg");
        if (!File.Exists(filePath))
        {
            using var httpClient = new HttpClient();
            await using var stream = await httpClient.GetStreamAsync(new Uri($"https://www.nuget.org/api/v2/package/{packageName}/{version}")).ConfigureAwait(false);

            filePath.CreateParentDirectory();
            await using var fileStream = File.OpenWrite(filePath);
            await stream.CopyToAsync(fileStream);
        }

        return filePath;
    }

    private static Task<NuGetPackageValidationResult> ValidateAsync(string packageName, params NuGetPackageValidationRule[] rules)
    {
        return ValidateAsync(packageName, excludedRuleIds: null, rules);
    }

    private static void AssertNoErrors(NuGetPackageValidationResult result)
    {
        result.Errors.Should().BeEmpty();
    }

    private static void AssertHasError(NuGetPackageValidationResult result, int expectedErrorCode)
    {
        result.Errors.Should().Contain(item => item.ErrorCode == expectedErrorCode);
    }

    [Fact]
    public async Task Validate_AssembliesMustBeOptimizedMustBeSet_Debug()
    {
        var result = await ValidateAsync("Debug.1.0.0.nupkg", NuGetPackageValidationRules.AssembliesMustBeOptimized);
        AssertHasError(result, ErrorCodes.AssemblyIsNotOptimized);
    }

    [Fact]
    public async Task Validate_AssembliesMustBeOptimizedMustBeSet_Release()
    {
        var result = await ValidateAsync("Release.1.0.0.nupkg", NuGetPackageValidationRules.AssembliesMustBeOptimized);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Description_DefaultDescription()
    {
        var result = await ValidateAsync("Release.1.0.0.nupkg", NuGetPackageValidationRules.DescriptionMustBeSet);
        AssertHasError(result, ErrorCodes.PackageHasDefaultDescription);
    }

    [Fact]
    public async Task Validate_Description_HasCustomDescription()
    {
        var result = await ValidateAsync("Release_Description.1.0.0.nupkg", NuGetPackageValidationRules.DescriptionMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Icon_NoIcon()
    {
        var result = await ValidateAsync("Release.1.0.0.nupkg", NuGetPackageValidationRules.IconMustBeSet);
        AssertHasError(result, ErrorCodes.IconNotSet);
    }

    [Fact]
    public async Task Validate_Icon_IconUrl()
    {
        var result = await ValidateAsync("Release_IconUrl.1.0.0.nupkg", NuGetPackageValidationRules.IconMustBeSet);
        AssertHasError(result, ErrorCodes.UseDeprecatedIconUrl);
    }

    [Fact]
    public async Task Validate_Icon_InvalidFileExtension()
    {
        var result = await ValidateAsync("Release_Icon_WrongExtension.1.0.0.nupkg", NuGetPackageValidationRules.IconMustBeSet);
        AssertHasError(result, ErrorCodes.IconFileInvalidExtension);
    }

    [Fact]
    public async Task Validate_Icon_HasIcon()
    {
        var result = await ValidateAsync("Release_Icon.1.0.0.nupkg", NuGetPackageValidationRules.IconMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Icon_HasIconAndIconUrl()
    {
        var result = await ValidateAsync("Release_Icon_IconUrl.1.0.0.nupkg", NuGetPackageValidationRules.IconMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Readme_NoReadme()
    {
        var result = await ValidateAsync("Release.1.0.0.nupkg", NuGetPackageValidationRules.ReadmeMustBeSet);
        AssertHasError(result, ErrorCodes.ReadmeNotSet);
    }

    [Fact]
    public async Task Validate_Readme_HasReadme()
    {
        var result = await ValidateAsync("Release_Readme.1.0.0.nupkg", NuGetPackageValidationRules.ReadmeMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_License_LicenseNotSet()
    {
        var result = await ValidateAsync("Release.1.0.0.nupkg", NuGetPackageValidationRules.LicenseMustBeSet);
        AssertHasError(result, ErrorCodes.LicenseNotSet);
    }

    [Fact]
    public async Task Validate_License_LicenseUrl()
    {
        var result = await ValidateAsync("Release_LicenseUrl.1.0.0.nupkg", NuGetPackageValidationRules.LicenseMustBeSet);
        AssertHasError(result, ErrorCodes.UseDeprecatedLicenseUrl);
    }

    [Fact]
    public async Task Validate_License_LicenseExpression()
    {
        var result = await ValidateAsync("Release_LicenseExpression.1.0.0.nupkg", NuGetPackageValidationRules.LicenseMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_License_LicenseFile()
    {
        var result = await ValidateAsync("Release_License.1.0.0.nupkg", NuGetPackageValidationRules.LicenseMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Author_DefaultAuthor()
    {
        var result = await ValidateAsync("Release_DefaultAuthor.1.0.0.nupkg", NuGetPackageValidationRules.AuthorMustBeSet);
        AssertHasError(result, ErrorCodes.DefaultAuthorSet);
    }

    [Fact]
    public async Task Validate_Author_AuthorSet()
    {
        var result = await ValidateAsync("Release_Author.1.0.0.nupkg", NuGetPackageValidationRules.AuthorMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Deterministic_NonDeterministic()
    {
        var result = await ValidateAsync("Release_NonDeterministic_Pdb.1.0.0.nupkg", NuGetPackageValidationRules.Symbols);
        AssertHasError(result, ErrorCodes.NonDeterministic);
    }

    [Fact]
    public async Task Validate_Deterministic_Embedded()
    {
        var result = await ValidateAsync("Release_Deterministic_Embedded.1.0.0.nupkg", NuGetPackageValidationRules.Symbols);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Deterministic_Embedded_NoSources()
    {
        var result = await ValidateAsync("Release_Deterministic_Embedded_SourceNotEmbedded.1.0.0.nupkg", NuGetPackageValidationRules.Symbols);
        AssertHasError(result, ErrorCodes.SourceFileNotAccessible);
    }

    [Fact]
    public async Task Validate_Deterministic_Pdb()
    {
        var result = await ValidateAsync("Release_Deterministic_Pdb.1.0.0.nupkg", [119], NuGetPackageValidationRules.Symbols);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Deterministic_Snupkg()
    {
        var result = await ValidateAsync("Release_Deterministic_Snupkg.1.0.0.nupkg", NuGetPackageValidationRules.Symbols);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Deterministic_Embedded_SourceLink()
    {
        var result = await ValidateAsync("meziantou.framework.win32.credentialmanager.1.4.2.nupkg", [119], NuGetPackageValidationRules.Symbols);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_CompilerFlags_NotPresent()
    {
        var result = await ValidateAsync("meziantou.framework.2.6.0.nupkg", NuGetPackageValidationRules.Symbols);
        AssertHasError(result, ErrorCodes.CompilerFlagsNotPresent);
    }

    [Fact]
    public async Task Validate_Symbols_FullPdb()
    {
        var result = await ValidateAsync("Release_Deterministic_Pdb_Full.1.0.0.nupkg", NuGetPackageValidationRules.Symbols);
        AssertHasError(result, ErrorCodes.FullPdb);
    }

    [Fact]
    public async Task Validate_XmlDocumentation_NotPresent()
    {
        var result = await ValidateAsync("Debug.1.0.0.nupkg", NuGetPackageValidationRules.XmlDocumentationMustBePresent);
        AssertHasError(result, ErrorCodes.XmlDocumentationNotFound);
    }

    [Fact]
    public async Task Validate_XmlDocumentation_NotPresent_Failure()
    {
        var result = await ValidateAsync("Release_NonDeterministic_Pdb.1.0.0.nupkg", NuGetPackageValidationRules.XmlDocumentationMustBePresent);
        AssertHasError(result, ErrorCodes.XmlDocumentationNotFound);
    }

    [Fact]
    public async Task Validate_XmlDocumentation_Present()
    {
        var result = await ValidateAsync("Release_XmlDocumentation.1.0.0.nupkg", NuGetPackageValidationRules.XmlDocumentationMustBePresent);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_WithSymbolsServer()
    {
        var path = await DownloadPackageAsync("Newtonsoft.Json", "13.0.2");
        var result = await ValidateAsync(path, excludedRuleIds: [ErrorCodes.FileHashIsNotValid], rules: [NuGetPackageValidationRules.Symbols]);
        AssertNoErrors(result);
    }
}
