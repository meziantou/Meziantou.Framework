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

    private sealed record Result(int ExitCode, string Output, NuGetPackageValidationResult ValidationResult);

    private async Task<Result> RunValidation(params string[] arguments)
    {
        var console = new StringBuilderConsole();
        var exitCode = await Program.MainImpl(arguments, console);

        NuGetPackageValidationResult deserializedResult = null;
        try
        {
            deserializedResult = JsonSerializer.Deserialize<NuGetPackageValidationResult>(console.Output);
        }
        catch
        {
        }

        _testOutputHelper.WriteLine(console.Output);
        return new Result(exitCode, console.Output, deserializedResult);
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
