using System.Reflection;
using System.Runtime.CompilerServices;
using Meziantou.Framework;
using Meziantou.Framework.InlineSnapshotTesting;
using Meziantou.Framework.PublicApiGenerator;
using Meziantou.Framework.PublicApiGenerator.Tool;
using Xunit.Sdk;

namespace Meziantou.Framework.PublicApiGenerator.Tests;

[Collection("Tool")] // Ensure tests run sequentially
public sealed class PublicApiGeneratorTests
{
    [Fact]
    public async Task EmptyClass()
    {
        await Validate("""
            public class Sample
            {
            }
            """, """
            public class Sample
            {
            }
            """);
    }

    [Fact]
    public async Task Property_GetSet()
    {
        await Validate("""
            public class Sample
            {
                public string Name { get; set; }
            }
            """, """
            public class Sample
            {
                public string Name { get; set; }
            }
            """);
    }

    [InlineSnapshotAssertion(nameof(expected))]
    private static async Task Validate(string source, string expected, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();

        // Build the project
        var sourceProjectDirectory = temporaryDirectory / "source";
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "project.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile(sourceProjectDirectory / "Sample.cs", source);

        var assemblyPath = await Compile(sourceProjectDirectory);

        // Generate the API files using both reflection and metadata
        var reflectionFiles = PublicApiStubGenerator.GenerateFiles(
            Assembly.LoadFile(assemblyPath),
            new PublicApiGeneratorOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
            });
        var metadataFiles = PublicApiStubGenerator.GenerateFiles(
            assemblyPath,
            new PublicApiGeneratorOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
            });

        var reflectionContent = SerializeFiles(reflectionFiles);
        var metadataContent = SerializeFiles(metadataFiles);
        Assert.Equal(reflectionContent, metadataContent);
        InlineSnapshot.Validate(reflectionContent, expected, filePath, lineNumber);

        // Ensure the generated files are compilable
        var generatedDirectory = temporaryDirectory / "generated";
        temporaryDirectory.CreateTextFile(generatedDirectory / "project.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
            </Project>
            """);
        foreach (var file in reflectionFiles)
        {
            temporaryDirectory.CreateTextFile(generatedDirectory / file.RelativePath, file.Content);
        }

        await Compile(generatedDirectory);

        static string SerializeFiles(IReadOnlyList<PublicApiGeneratedFile> files)
        {
            return string.Join(
                "\n\n",
                files
                    .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                    .Select(file => file.Content.TrimEnd('\r', '\n')));
        }
    }

    private static async Task<FullPath> Compile(FullPath temporaryDirectory)
    {
        var projectPath = Assert.Single(Directory.EnumerateFiles(temporaryDirectory, "*.csproj", SearchOption.TopDirectoryOnly));

        var outputPath = temporaryDirectory / "bin";

        await RunDotNetAsync(temporaryDirectory, ["restore", projectPath, "-nologo", "--disable-build-servers"]);
        await RunDotNetAsync(temporaryDirectory, ["build", projectPath, "-nologo", "--disable-build-servers", "--no-restore", "--output", outputPath, "/p:AssemblyName=Source"]);
        return outputPath / "bin" / "Source.dll";

        static async Task RunDotNetAsync(string workingDirectory, IReadOnlyList<string> arguments)
        {
            var processResult = await ProcessWrapper.Create("dotnet")
                .WithArguments(arguments)
                .WithWorkingDirectory(workingDirectory)
                .WithEnvironmentVariables(env => env.Set("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1"))
                .WithValidation(ProcessValidationMode.None)
                .ExecuteBufferedAsync(XunitCancellationToken);

            if (!processResult.ExitCode.IsSuccess)
            {
                var standardOutput = string.Join('\n', processResult.Output.StandardOutput.Select(line => line.Text));
                var standardError = string.Join('\n', processResult.Output.StandardError.Select(line => line.Text));
                throw new XunitException($"Command failed: dotnet {string.Join(' ', arguments)}\nstdout:\n{standardOutput}\nstderr:\n{standardError}");
            }
        }
    }
}
