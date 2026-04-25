using System.Diagnostics;
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

        await RunDotNetAsync(temporaryDirectory, $"restore \"{projectPath}\" -nologo --disable-build-servers");
        await RunDotNetAsync(temporaryDirectory, $"build \"{projectPath}\" -nologo --disable-build-servers --no-restore --output \"{outputPath}\" /p:AssemblyName=Source");
        return outputPath / "bin" / "Source.dll";
    }

    private static async Task RunDotNetAsync(string workingDirectory, string arguments)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        processStartInfo.EnvironmentVariables["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = Process.Start(processStartInfo) ?? throw new XunitException("Cannot start dotnet process");
        var stdOutTask = process.StandardOutput.ReadToEndAsync(XunitCancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(XunitCancellationToken);
        await process.WaitForExitAsync(XunitCancellationToken);

        var standardOutput = await stdOutTask;
        var standardError = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new XunitException($"Command failed: dotnet {arguments}\nstdout:\n{standardOutput}\nstderr:\n{standardError}");
        }
    }
}
