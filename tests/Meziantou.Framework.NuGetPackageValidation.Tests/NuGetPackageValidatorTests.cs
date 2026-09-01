using System.IO.Compression;
using Meziantou.Framework.NuGetPackageValidation.Rules;

namespace Meziantou.Framework.NuGetPackageValidation.Tests;

public sealed class NuGetPackageValidatorTests
{
    private static Task<NuGetPackageValidationResult> ValidateAsync(string packageName, int[]? excludedRuleIds, params NuGetPackageValidationRule[] rules)
    {
        var path = FullPath.FromPath(typeof(NuGetPackageValidatorTests).Assembly.Location).Parent / "Packages" / packageName;
        return ValidateAsync(path, excludedRuleIds, rules);
    }

    private static async Task<NuGetPackageValidationResult> ValidateAsync(FullPath packagePath, int[]? excludedRuleIds, params NuGetPackageValidationRule[] rules)
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

    /// <summary>Validates a package built on the fly from the provided nuspec metadata. Rules that only read the
    /// nuspec can be exercised this way without adding a binary fixture to the Packages folder.</summary>
    private static async Task<NuGetPackageValidationResult> ValidateMetadataAsync(string metadata, params NuGetPackageValidationRule[] rules)
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var packagePath = temporaryDirectory / "package.nupkg";

        await using (var stream = File.Create(packagePath))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            await using var entry = archive.CreateEntry("Sample.nuspec").Open();
            await using var writer = new StreamWriter(entry);
            await writer.WriteAsync($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
                  <metadata>
                    <id>Sample</id>
                    <version>1.0.0</version>
                    <authors>Sample author</authors>
                    <description>Sample description</description>
                {metadata}
                  </metadata>
                </package>
                """);
        }

        return await ValidateAsync(packagePath, excludedRuleIds: null, rules);
    }

    private static void AssertNoErrors(NuGetPackageValidationResult result)
    {
        Assert.Empty(result.Errors);
    }

    private static void AssertHasError(NuGetPackageValidationResult result, int expectedErrorCode)
    {
        Assert.Contains(result.Errors, item => item.ErrorCode == expectedErrorCode);
    }

    [Fact]
    public async Task Validate_PackageFileNotFound()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var result = await ValidateAsync(temporaryDirectory / "missing.nupkg", excludedRuleIds: null, NuGetPackageValidationRules.Default.ToArray());
        AssertHasError(result, ErrorCodes.FileNotFound);
    }

    [Fact]
    public async Task Validate_PackageFileNotFound_ExcludedRuleId()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var result = await ValidateAsync(temporaryDirectory / "missing.nupkg", [ErrorCodes.FileNotFound], NuGetPackageValidationRules.Default.ToArray());
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_PackageIsNotAValidArchive()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var packagePath = await temporaryDirectory.CreateTextFileAsync("corrupted.nupkg", "This is not a zip archive");
        var result = await ValidateAsync(packagePath, excludedRuleIds: null, NuGetPackageValidationRules.Default.ToArray());
        AssertHasError(result, ErrorCodes.InvalidPackage);
    }

    [Fact]
    public async Task Validate_PackageIsNotAValidArchive_ExcludedRuleId()
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var packagePath = await temporaryDirectory.CreateTextFileAsync("corrupted.nupkg", "This is not a zip archive");
        var result = await ValidateAsync(packagePath, [ErrorCodes.InvalidPackage], NuGetPackageValidationRules.Default.ToArray());
        AssertNoErrors(result);
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
    public async Task Validate_AssembliesMustBeOptimizedMustBeSet_Debug_ModuleAttribute()
    {
        // The assembly applies an attribute it declares itself to its own module, so the CustomAttribute
        // table holds a MethodDefinition constructor that sorts before the assembly-level DebuggableAttribute
        var result = await ValidateAsync("Debug_ModuleAttribute.1.0.0.nupkg", NuGetPackageValidationRules.AssembliesMustBeOptimized);
        AssertHasError(result, ErrorCodes.AssemblyIsNotOptimized);
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
    public async Task Validate_Icon_HasIcon_Backslash()
    {
        var result = await ValidateAsync("Release_Icon_Backslash.1.0.0.nupkg", NuGetPackageValidationRules.IconMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Icon_HasIconAndIconUrl()
    {
        var result = await ValidateAsync("Release_Icon_IconUrl.1.0.0.nupkg", NuGetPackageValidationRules.IconMustBeSet);
        AssertNoErrors(result);
    }

    /// <summary>Builds a package declaring <paramref name="iconPath"/> as its icon, with <paramref name="iconContent"/> as
    /// the content of that file, and validates it with the icon rule.</summary>
    private static async Task<NuGetPackageValidationResult> ValidateIconAsync(string iconPath, byte[] iconContent)
    {
        using var temporaryDirectory = TemporaryDirectory.Create();
        var packagePath = temporaryDirectory / "package.nupkg";

        await using (var stream = File.Create(packagePath))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            await using (var nuspec = archive.CreateEntry("Sample.nuspec").Open())
            {
                await using var writer = new StreamWriter(nuspec);
                await writer.WriteAsync($"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
                      <metadata>
                        <id>Sample</id>
                        <version>1.0.0</version>
                        <authors>Sample author</authors>
                        <description>Sample description</description>
                        <icon>{iconPath}</icon>
                      </metadata>
                    </package>
                    """);
            }

            await using var iconEntry = archive.CreateEntry(iconPath).Open();
            await iconEntry.WriteAsync(iconContent);
        }

        return await ValidateAsync(packagePath, excludedRuleIds: null, NuGetPackageValidationRules.IconMustBeSet);
    }

    [Theory]
    // The extension is compared without case sensitivity
    [InlineData("images/icon.png")]
    [InlineData("images/icon.PNG")]
    public async Task Validate_Icon_Png(string iconPath)
    {
        var result = await ValidateIconAsync(iconPath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        AssertNoErrors(result);
    }

    [Theory]
    // JFIF
    [InlineData("images/icon.jpg", (byte)0xE0)]
    // Exif
    [InlineData("images/icon.jpg", (byte)0xE1)]
    // Raw JPEG stream starting with a quantization table
    [InlineData("images/icon.jpg", (byte)0xDB)]
    [InlineData("images/icon.JPEG", (byte)0xE1)]
    public async Task Validate_Icon_Jpeg(string iconPath, byte marker)
    {
        var result = await ValidateIconAsync(iconPath, [0xFF, 0xD8, 0xFF, marker, 0x00, 0x10]);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Icon_ContentDoesNotMatchTheExtension()
    {
        var result = await ValidateIconAsync("images/icon.jpg", [0x89, 0x50, 0x4E, 0x47]);
        AssertHasError(result, ErrorCodes.IconFileInvalidExtension);
    }

    [Fact]
    public async Task Validate_Icon_UnsupportedFormat()
    {
        var result = await ValidateIconAsync("images/icon.gif", [0x47, 0x49, 0x46, 0x38]);
        AssertHasError(result, ErrorCodes.IconFileFormatNotSupported);
    }

    [Fact]
    public async Task Validate_ProjectUrl_ProjectUrlSetWithoutRepository()
    {
        var result = await ValidateMetadataAsync("""    <projectUrl>https://www.example.com/</projectUrl>""", NuGetPackageValidationRules.ProjectUrlMustBeSet);
        Assert.DoesNotContain(result.Errors, item => item.ErrorCode == ErrorCodes.ProjectUrlNotSet);
    }

    [Fact]
    public async Task Validate_ProjectUrl_RepositorySetWithoutProjectUrl()
    {
        var result = await ValidateMetadataAsync("""    <repository type="git" url="https://www.example.com/" />""", NuGetPackageValidationRules.ProjectUrlMustBeSet);
        AssertHasError(result, ErrorCodes.ProjectUrlNotSet);
    }

    [Fact]
    public async Task Validate_ProjectUrl_NotSet()
    {
        var result = await ValidateMetadataAsync("", NuGetPackageValidationRules.ProjectUrlMustBeSet);
        AssertHasError(result, ErrorCodes.ProjectUrlNotSet);
    }

    [Fact]
    public async Task Validate_ProjectUrl_NotAnHttpUrl()
    {
        var result = await ValidateMetadataAsync("""    <projectUrl>ftp://www.example.com/</projectUrl>""", NuGetPackageValidationRules.ProjectUrlMustBeSet);
        AssertHasError(result, ErrorCodes.ProjectUrlNotAccessible);
        Assert.DoesNotContain(result.Errors, item => item.ErrorCode == ErrorCodes.ProjectUrlNotSet);
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
    public async Task Validate_Repository_NotSet()
    {
        // NuspecReader.GetRepositoryMetadata returns an empty instance rather than null when the element is missing,
        // so the individual fields are reported instead of ErrorCodes.RepositoryNotSet
        var result = await ValidateAsync("Release.1.0.0.nupkg", NuGetPackageValidationRules.RepositoryMustBeSet);
        AssertHasError(result, ErrorCodes.RepositoryTypeNotSet);
        AssertHasError(result, ErrorCodes.RepositoryUrlNotSet);
        AssertHasError(result, ErrorCodes.RepositoryCommitNotSet);
    }

    [Fact]
    public async Task Validate_Repository_TypeOnly()
    {
        var result = await ValidateAsync("Release_RepositoryType.1.0.0.nupkg", NuGetPackageValidationRules.RepositoryMustBeSet);
        AssertHasError(result, ErrorCodes.RepositoryUrlNotSet);
        AssertHasError(result, ErrorCodes.RepositoryCommitNotSet);
    }

    [Fact]
    public async Task Validate_Repository_TypeUrlAndCommit()
    {
        var result = await ValidateAsync("Release_RepositoryType_RepositoryUrl_RepositoryCommit.1.0.0.nupkg", NuGetPackageValidationRules.RepositoryMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_RepositoryBranch_NotSet()
    {
        var result = await ValidateAsync("Release_RepositoryType_RepositoryUrl_RepositoryCommit.1.0.0.nupkg", NuGetPackageValidationRules.RepositoryBranchMustBeSet);
        AssertHasError(result, ErrorCodes.RepositoryBranchNotSet);
    }

    [Fact]
    public async Task Validate_RepositoryBranch_Set()
    {
        var result = await ValidateAsync("Release_RepositoryType_RepositoryUrl_RepositoryCommit_RepositoryBranch.1.0.0.nupkg", NuGetPackageValidationRules.RepositoryBranchMustBeSet);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_Tags_NotSet()
    {
        var result = await ValidateAsync("Release.1.0.0.nupkg", NuGetPackageValidationRules.TagsMustBeSet);
        AssertHasError(result, ErrorCodes.TagsNotSet);
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

    [Theory]
    // Exact match
    [InlineData("/_/src/Foo.cs", "https://example.com/raw/src/Foo.cs")]
    // No match
    [InlineData("/_/src/Bar.cs", null)]
    public void SourceLink_ExactMatch(string file, string? expected)
    {
        var json = new SymbolsValidationRule.SourceLinkJson
        {
            Documents = new(StringComparer.Ordinal) { ["/_/src/Foo.cs"] = "https://example.com/raw/src/Foo.cs" },
        };

        Assert.Equal(expected, json.GetUrl(file));
    }

    [Theory]
    [InlineData("/_/src/Foo.cs", "https://example.com/raw/src/Foo.cs")]
    [InlineData("/_/src/sub/Foo.cs", "https://example.com/raw/src/sub/Foo.cs")]
    // Backslashes of the matched value are normalized
    [InlineData("/_/src/sub\\Foo.cs", "https://example.com/raw/src/sub/Foo.cs")]
    // The key is anchored: it must match the whole path, not a suffix of it
    [InlineData("/other/_/src/Foo.cs", null)]
    // The matched value cannot be empty of the prefix
    [InlineData("/_/", null)]
    public void SourceLink_Wildcard(string file, string? expected)
    {
        var json = new SymbolsValidationRule.SourceLinkJson
        {
            Documents = new(StringComparer.Ordinal) { ["/_/src/*"] = "https://example.com/raw/src/*" },
        };

        Assert.Equal(expected, json.GetUrl(file));
    }

    [Fact]
    public void SourceLink_OnlyTheFirstWildcardOfTheUrlIsReplaced()
    {
        var json = new SymbolsValidationRule.SourceLinkJson
        {
            Documents = new(StringComparer.Ordinal) { ["/_/*"] = "https://example.com/*?ref=*" },
        };

        Assert.Equal("https://example.com/Foo.cs?ref=*", json.GetUrl("/_/Foo.cs"));
    }

    [Fact]
    public void SourceLink_KeyWithSeveralWildcardsIsIgnored()
    {
        // The Source Link specification allows a single wildcard. Matching such a key with a regex built from
        // the key made the lookup vulnerable to catastrophic backtracking, and it ran without a timeout.
        var json = new SymbolsValidationRule.SourceLinkJson
        {
            Documents = new(StringComparer.Ordinal) { [new string('*', 8) + "Z"] = "https://example.com/*" },
        };

        Assert.Null(json.GetUrl(new string('a', 40)));
    }

    [Fact]
    public async Task Validate_PackageIdAvailableOnNuGetOrg_PackageExists()
    {
        var result = await ValidateAsync("meziantou.framework.2.6.0.nupkg", NuGetPackageValidationRules.PackageIdAvailableOnNuGetOrg);
        AssertHasError(result, ErrorCodes.PackageIdExistsOnNuGetOrg);
    }

    [Fact]
    public async Task Validate_PackageIdAvailableOnNuGetOrg_PackageDoesNotExist()
    {
        var result = await ValidateAsync("Release_Author.1.0.0.nupkg", NuGetPackageValidationRules.PackageIdAvailableOnNuGetOrg);
        AssertNoErrors(result);
    }

    [Fact]
    public async Task Validate_CancellationIsNotReportedAsAValidationError()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // The package has a project url, so the rule performs an HTTP request that observes the token
        var path = FullPath.FromPath(typeof(NuGetPackageValidatorTests).Assembly.Location).Parent / "Packages" / "meziantou.framework.2.6.0.nupkg";
        var options = new NuGetPackageValidationOptions();
        options.Rules.Add(NuGetPackageValidationRules.ProjectUrlMustBeSet);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NuGetPackageValidator.ValidateAsync(path, options, cts.Token));
    }

    [Fact]
    public async Task Validate_ErrorsKeepTheOrderTheyAreReportedIn()
    {
        // The package sets the repository type but neither the url nor the commit
        var result = await ValidateAsync("Release_RepositoryType.1.0.0.nupkg", NuGetPackageValidationRules.RepositoryMustBeSet);
        Assert.Equal([ErrorCodes.RepositoryUrlNotSet, ErrorCodes.RepositoryCommitNotSet], result.Errors.Select(item => item.ErrorCode));
    }

    [Fact]
    public async Task Validate_WithSymbolsServer()
    {
        // Downloading symbols can be flaky on CI, but the last attempt must report its failure
        const int MaxAttempts = 10;
        for (var i = 1; ; i++)
        {
            try
            {
                var path = await DownloadPackageAsync("Newtonsoft.Json", "13.0.2");
                var result = await ValidateAsync(path, excludedRuleIds: [ErrorCodes.FileHashIsNotValid], rules: [NuGetPackageValidationRules.Symbols]);
                AssertNoErrors(result);
                return;
            }
            catch when (i < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), XunitCancellationToken);
            }
        }
    }
}
