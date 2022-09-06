using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace Meziantou.Framework.NuGetPackageValidation.Tool.Tests;

[Collection("Tool")] // Ensure tests run sequentially
public sealed class NugetPackageValidateToolTests
{
    private sealed record Result(int ExitCode, string Output, NuGetPackageValidationResult ValidationResult);

    private static async Task<Result> RunValidation(params string[] arguments)
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

        return new Result(exitCode, console.Output, deserializedResult);
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
