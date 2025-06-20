using System.Text.Json;
using Xunit;
using Xunit.v3;

namespace Meziantou.Framework.NuGetPackageValidation.Tool.Tests;

[Collection("Tool")] // Ensure tests run sequentially
public sealed class NugetPackageValidateToolTests(ITestOutputHelper testOutputHelper)
{
    private sealed record RunResult(int ExitCode, string Output, ValidationResult ValidationResults, NuGetPackageValidationResult ValidationResult);

    private sealed record ValidationResult(bool IsValid, Dictionary<string, NuGetPackageValidationResult> Packages);

    private async Task<RunResult> RunValidation(params string[] arguments)
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(arguments, console.ConfigureConsole);

        Assert.True(console.Output.Count(c => c == '\n') > 2); // Check if output is written indented

        ValidationResult deserializedResult = null;
        try
        {
            deserializedResult = JsonSerializer.Deserialize<ValidationResult>(console.Output);
        }
        catch
        {
        }

        return new RunResult(exitCode, console.Output, deserializedResult, deserializedResult?.Packages.FirstOrDefault().Value);
    }

    [Fact]
    public async Task Help()
    {
        var result = await RunValidation("--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("meziantou.validate-nuget-package", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoPackage()
    {
        var result = await RunValidation();
        Assert.Equal(1, result.ExitCode);
        Assert.Null(result.ValidationResult);
    }

    [Fact]
    public async Task TestPackage()
    {
        var result = await RunValidation("Packages/Debug.1.0.0.nupkg");
        Assert.Equal(1, result.ExitCode);
        Assert.False(result.ValidationResult.IsValid);
        Assert.Contains(result.ValidationResult.Errors, item => item.ErrorCode == 81);
    }

    [Fact]
    public async Task TestPackage_Multiple()
    {
        var path1 = FullPath.FromPath("Packages/Debug.1.0.0.nupkg");
        var path2 = FullPath.FromPath("Packages/Release.1.0.0.nupkg");
        var result = await RunValidation(path1, path2);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(2, result.ValidationResults.Packages.Count);
        Assert.False(result.ValidationResults.Packages[path1].IsValid);
        Assert.Contains(result.ValidationResults.Packages[path1].Errors, item => item.ErrorCode == 81);
        Assert.False(result.ValidationResults.Packages[path2].IsValid);
        Assert.Contains(result.ValidationResults.Packages[path2].Errors, item => item.ErrorCode == 101);
    }

    [Fact]
    public async Task ExcludedRuleIds()
    {
        var result = await RunValidation("Packages/Debug.1.0.0.nupkg", "--excluded-rule-ids", "81,73;101", "--excluded-rule-ids", "75");
        Assert.Equal(1, result.ExitCode);
        Assert.False(result.ValidationResult.IsValid);
        Assert.DoesNotContain(result.ValidationResult.Errors, item => item.ErrorCode == 73);
        Assert.DoesNotContain(result.ValidationResult.Errors, item => item.ErrorCode == 75);
        Assert.DoesNotContain(result.ValidationResult.Errors, item => item.ErrorCode == 81);
        Assert.DoesNotContain(result.ValidationResult.Errors, item => item.ErrorCode == 101);
    }

    [Fact]
    public async Task CustomRules()
    {
        var result = await RunValidation("Packages/Release_Author.1.0.0.nupkg", "--rules", "AssembliesMustBeOptimized,AuthorMustBeSet");
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.ValidationResult.IsValid);
        Assert.Empty(result.ValidationResult.Errors);
    }

    [Fact]
    public async Task CustomRules_UnknownRules()
    {
        var result = await RunValidation("Packages/Release_Author.1.0.0.nupkg", "--rules", "Unknown");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Invalid rule 'Unknown'", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.ValidationResult);
    }

    [Fact]
    public async Task CustomRules_Multiple()
    {
        var result = await RunValidation("Packages/Release_Author.1.0.0.nupkg", "--rules", "AssembliesMustBeOptimized", "--rules", "AuthorMustBeSet");
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.ValidationResult.IsValid);
        Assert.Empty(result.ValidationResult.Errors);
    }
}
