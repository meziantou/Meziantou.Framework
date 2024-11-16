using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using Xunit.Abstractions;

namespace Meziantou.Framework.NuGetPackageValidation.Tool.Tests;

[Collection("Tool")] // Ensure tests run sequentially
public sealed class NugetPackageValidateToolTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public NugetPackageValidateToolTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private sealed record RunResult(int ExitCode, string Output, ValidationResult ValidationResults, NuGetPackageValidationResult ValidationResult);

    private sealed record ValidationResult(bool IsValid, Dictionary<string, NuGetPackageValidationResult> Packages);

    private async Task<RunResult> RunValidation(params string[] arguments)
    {
        var console = new StringBuilderConsole();
        var exitCode = await Program.MainImpl(arguments, console);

        Assert.True(console.Output.Count(c => c == '\n') > 2); // Check if output is written indented

        ValidationResult deserializedResult = null;
        try
        {
            deserializedResult = JsonSerializer.Deserialize<ValidationResult>(console.Output);
        }
        catch
        {
        }

        _testOutputHelper.WriteLine(console.Output);
        return new RunResult(exitCode, console.Output, deserializedResult, deserializedResult?.Packages.FirstOrDefault().Value);
    }

    [Fact]
    public async Task Help()
    {
        var result = await RunValidation("--help");
        using (new AssertionScope())
        {
            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("meziantou.validate-nuget-package");
        }
    }

    [Fact]
    public async Task NoPackage()
    {
        var result = await RunValidation();
        using (new AssertionScope())
        {
            result.ExitCode.Should().Be(1);
            result.ValidationResult.Should().BeNull();
        }
    }

    [Fact]
    public async Task TestPackage()
    {
        var result = await RunValidation("Packages/Debug.1.0.0.nupkg");
        using (new AssertionScope())
        {
            result.ExitCode.Should().Be(1);
            result.ValidationResult.IsValid.Should().BeFalse();
            result.ValidationResult.Errors.Should().Contain(item => item.ErrorCode == 81);
        }
    }

    [Fact]
    public async Task TestPackage_Multiple()
    {
        var path1 = FullPath.FromPath("Packages/Debug.1.0.0.nupkg");
        var path2 = FullPath.FromPath("Packages/Release.1.0.0.nupkg");
        var result = await RunValidation(path1, path2);
        using (new AssertionScope())
        {
            result.ExitCode.Should().Be(1);
            result.ValidationResults.Packages.Should().HaveCount(2);

            result.ValidationResults.Packages[path1].IsValid.Should().BeFalse();
            result.ValidationResults.Packages[path1].Errors.Should().Contain(item => item.ErrorCode == 81);

            result.ValidationResults.Packages[path2].IsValid.Should().BeFalse();
            result.ValidationResults.Packages[path2].Errors.Should().Contain(item => item.ErrorCode == 101);
        }
    }

    [Fact]
    public async Task ExcludedRuleIds()
    {
        var result = await RunValidation("Packages/Debug.1.0.0.nupkg", "--excluded-rule-ids", "81,73;101", "--excluded-rule-ids", "75");
        using (new AssertionScope())
        {
            result.ExitCode.Should().Be(1);
            result.ValidationResult.IsValid.Should().BeFalse();
            result.ValidationResult.Errors.Should().NotContain(item => item.ErrorCode == 73);
            result.ValidationResult.Errors.Should().NotContain(item => item.ErrorCode == 75);
            result.ValidationResult.Errors.Should().NotContain(item => item.ErrorCode == 81);
            result.ValidationResult.Errors.Should().NotContain(item => item.ErrorCode == 101);
        }
    }

    [Fact]
    public async Task CustomRules()
    {
        var result = await RunValidation("Packages/Release_Author.1.0.0.nupkg", "--rules", "AssembliesMustBeOptimized,AuthorMustBeSet");
        using (new AssertionScope())
        {
            result.ExitCode.Should().Be(0);
            result.ValidationResult.IsValid.Should().BeTrue();
            result.ValidationResult.Errors.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CustomRules_UnknownRules()
    {
        var result = await RunValidation("Packages/Release_Author.1.0.0.nupkg", "--rules", "Unknown");
        using (new AssertionScope())
        {
            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain("Invalid rule 'Unknown'");
            result.ValidationResult.Should().BeNull();
        }
    }

    [Fact]
    public async Task CustomRules_Multiple()
    {
        var result = await RunValidation("Packages/Release_Author.1.0.0.nupkg", "--rules", "AssembliesMustBeOptimized", "--rules", "AuthorMustBeSet");
        using (new AssertionScope())
        {
            result.ExitCode.Should().Be(0);
            result.ValidationResult.IsValid.Should().BeTrue();
            result.ValidationResult.Errors.Should().BeEmpty();
        }
    }
}
