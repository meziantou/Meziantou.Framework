using System.Diagnostics;
using System.Reflection;
using Meziantou.Framework;
using Meziantou.Framework.PublicApiGenerator;
using Meziantou.Framework.PublicApiGenerator.Tool;
using Xunit.Sdk;

namespace Meziantou.Framework.PublicApiGenerator.Tests;

[Collection("Tool")] // Ensure tests run sequentially
public sealed class PublicApiGeneratorTests
{
    private const string SampleSource = """
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.InteropServices;

        namespace Sample;

        file class FileOnlyType
        {
            public int Value => 1;
        }

        internal class InternalType
        {
            public int Value => 1;
        }

        [StructLayout(LayoutKind.Sequential)]
        public partial class ApiClass
        {
            public required int A { get; init; }

            [return: NotNullIfNotNull(nameof(input))]
            public string? Identity(string? input) => input;

            public ref int RefReturn(ref int value) => ref value;

            public void InRefOut(in int a, ref int b, out int c)
            {
                c = a + b;
            }

            [DllImport("kernel32.dll")]
            public static extern IntPtr GetCurrentThread();

            [LibraryImport("kernel32.dll")]
            public static partial int Beep(int dwFreq, int dwDuration);

            protected internal virtual int VirtualMethod() => 0;

            protected class NestedProtectedType
            {
                public int Value => 0;
            }
        }

        public interface IApiInterface
        {
            int AbstractMethod();

            int DefaultMethod() => 42;

            static virtual int StaticVirtual() => 7;
        }

        public delegate int ApiDelegate(int value);

        public struct ApiStruct
        {
            public int Value;
        }

        public readonly record struct ApiRecordStruct(int Value);

        public record ApiRecord(int Value);

        public enum ApiEnum
        {
            None = 0,
            One = 1,
        }
        """;

    [Fact]
    public async Task MetadataAndReflectionProduceEquivalentModel()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSampleProjectAsync(temporaryDirectory);

        var metadataModel = PublicApiStubGenerator.ReadModel(assemblyPath);
        var reflectionAssembly = Assembly.LoadFile(assemblyPath);
        var reflectionModel = PublicApiStubGenerator.ReadModel(reflectionAssembly);

        Assert.Equal(
            metadataModel.Types.Select(static type => type.QualifiedName).OrderBy(static value => value, StringComparer.Ordinal),
            reflectionModel.Types.Select(static type => type.QualifiedName).OrderBy(static value => value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ModuleAndTypeOverloadsUseReflectionModel()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSampleProjectAsync(temporaryDirectory);
        var assembly = Assembly.LoadFile(assemblyPath);

        var assemblyModel = PublicApiStubGenerator.ReadModel(assembly);
        var moduleModel = PublicApiStubGenerator.ReadModel(assembly.ManifestModule);
        var typeModel = PublicApiStubGenerator.ReadModel(assembly.GetType("Sample.ApiClass", throwOnError: true)!);

        Assert.Equal(
            assemblyModel.Types.Select(static type => type.QualifiedName).OrderBy(static value => value, StringComparer.Ordinal),
            moduleModel.Types.Select(static type => type.QualifiedName).OrderBy(static value => value, StringComparer.Ordinal));
        Assert.Contains(typeModel.Types, static type => string.Equals(type.QualifiedName, "Sample.ApiClass", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedOutputCompiles(bool useAssemblyReader)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSampleProjectAsync(temporaryDirectory);
        var reflectionAssembly = useAssemblyReader ? Assembly.LoadFile(assemblyPath) : null;
        foreach (var layout in Enum.GetValues<PublicApiFileLayout>())
        {
            var outputDirectory = temporaryDirectory.GetFullPath($"generated-{(useAssemblyReader ? "reflection" : "metadata")}-{layout}");
            var options = new PublicApiGeneratorOptions
            {
                FileLayout = layout,
            };
            if (reflectionAssembly is null)
            {
                PublicApiStubGenerator.GenerateToDirectory(assemblyPath, outputDirectory, options);
            }
            else
            {
                PublicApiStubGenerator.GenerateToDirectory(reflectionAssembly, outputDirectory, options);
            }

            await CompileGeneratedProjectAsync(outputDirectory);
        }
    }

    [Fact]
    public async Task GeneratedOutputContainsRequiredMembers()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSampleProjectAsync(temporaryDirectory);
        var files = PublicApiStubGenerator.GenerateFiles(
            Assembly.LoadFile(assemblyPath),
            new PublicApiGeneratorOptions
            {
                FileLayout = PublicApiFileLayout.SingleFile,
            });
        var file = Assert.Single(files);
        Assert.Contains("required int A", file.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliGeneratesFiles()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var assemblyPath = await CompileSampleProjectAsync(temporaryDirectory);
        var outputDirectory = temporaryDirectory.GetFullPath("cli-output");

        var result = await Program.MainImpl(
            [
                "--input", assemblyPath,
                "--output", outputDirectory,
                "--file-layout", PublicApiFileLayout.OneFilePerType.ToString(),
            ],
            configure: null);

        Assert.Equal(0, result);
        Assert.NotEmpty(Directory.EnumerateFiles(outputDirectory, "*.cs", SearchOption.AllDirectories));
    }

    private static async Task<string> CompileSampleProjectAsync(TemporaryDirectory temporaryDirectory)
    {
        var projectDirectory = temporaryDirectory.GetFullPath("source");
        Directory.CreateDirectory(projectDirectory);

        var projectPath = projectDirectory / "Source.csproj";
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
            </Project>
            """, XunitCancellationToken);
        await File.WriteAllTextAsync(projectDirectory / "Sample.cs", SampleSource, XunitCancellationToken);

        await RunDotNetAsync(projectDirectory, $"build \"{projectPath}\" -nologo");
        var assemblyPath = projectDirectory / "bin" / "Debug" / "net8.0" / "Source.dll";
        Assert.True(File.Exists(assemblyPath), "Sample assembly should exist after build");
        return assemblyPath;
    }

    private static async Task CompileGeneratedProjectAsync(string generatedDirectory)
    {
        var projectPath = Path.Combine(generatedDirectory, "Generated.csproj");
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <LangVersion>preview</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
              </PropertyGroup>
            </Project>
            """, XunitCancellationToken);

        await RunDotNetAsync(generatedDirectory, $"build \"{projectPath}\" -nologo");
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
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(XunitCancellationToken);

        var standardOutput = await stdOutTask;
        var standardError = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new XunitException($"Command failed: dotnet {arguments}\nstdout:\n{standardOutput}\nstderr:\n{standardError}");
        }
    }
}
