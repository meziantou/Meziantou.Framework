#nullable enable

using System.Collections;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class SnapshotEndToEndTests
{
    public enum SnapshotTestFramework
    {
        Xunit,
        XunitV3,
        MSTest,
        NUnit,
        TUnit,
    }

    [Fact]
    public async Task Validate_EndToEnd_CreatesReceivedFile_WhenSnapshotFails()
    {
        var snapshotFiles = await AssertSnapshot(
            """
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void SampleTest()
                {
                    Snapshot.Validate("sample");
                }
            }
            """,
            expectFailure: true,
            existingFiles:
            [
                new SnapshotFile("__snapshots__/SampleTest_0.txt", "expected"u8.ToArray()),
            ]);

        AssertSnapshotContent(snapshotFiles, [
            ("__snapshots__/SampleTest_0.received.txt", "sample"),
            ("__snapshots__/SampleTest_0.txt", "expected"),
        ]);
    }

    private static void AssertSnapshotContent(SnapshotFile[] snapshotFiles, (string RelativePath, string Content)[] expected)
    {
        Assert.Equal(expected, snapshotFiles.Select(f => (f.RelativePath, f.ContentAsString)));
    }

    private static async Task<SnapshotFile[]> AssertSnapshot(
        [StringSyntax("c#-test")] string source,
        SnapshotTestFramework testFramework = SnapshotTestFramework.XunitV3,
        string? targetFramework = null,
        bool expectFailure = false,
        IReadOnlyList<SnapshotFile>? existingFiles = null)
    {
        await using var directory = TemporaryDirectory.Create();
        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        Assert.NotNull(dotnetPath);

        var snapshotProjectPath = GetRepositoryRoot() / "src" / "Meziantou.Framework.SnapshotTesting" / "Meziantou.Framework.SnapshotTesting.csproj";
        CreateTextFile("Project.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{targetFramework ?? GetTargetFramework()}}</TargetFramework>
                <Nullable>disable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                {{GetPackageReferences(testFramework)}}
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="{{snapshotProjectPath}}" />
              </ItemGroup>
            </Project>
            """);
        CreateTextFile("GlobalUsings.cs", GetGlobalUsings(testFramework));
        CreateTextFile("SnapshotIntegrationTests.cs", source);
        if (existingFiles is not null)
        {
            foreach (var existingFile in existingFiles)
            {
                CreateBinaryFile(existingFile.RelativePath, existingFile.Content);
            }
        }

        await ExecuteDotNet(directory.FullPath, dotnetPath, ["restore"], expectedExitCode: 0);
        await ExecuteDotNet(directory.FullPath, dotnetPath, ["build", "--no-restore"], expectedExitCode: 0);
        await ExecuteDotNet(directory.FullPath, dotnetPath, ["test", "--no-build", "--nologo", "-v", "minimal"], expectedExitCode: expectFailure ? 1 : 0);

        return GetGeneratedSnapshotFiles(directory.FullPath);

        FullPath CreateTextFile(string path, string content)
        {
            var fullPath = directory.GetFullPath(path);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        FullPath CreateBinaryFile(string path, byte[] data)
        {
            var fullPath = directory.GetFullPath(path);
            fullPath.CreateParentDirectory();
            File.WriteAllBytes(fullPath, data);
            return fullPath;
        }
    }

    private static FullPath GetRepositoryRoot([CallerFilePath] string? filePath = null)
    {
        var directory = FullPath.FromPath(Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Cannot resolve source directory."));
        return directory.Parent.Parent;
    }

    private static SnapshotFile[] GetGeneratedSnapshotFiles(FullPath rootPath)
    {
        return Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories)
                    .Select(path => path.Replace('\\', '/'))
                    .Where(path => path.Contains("/__snapshots__/", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)
                    .Select(file => new SnapshotFile(Path.GetRelativePath(rootPath, file).Replace('\\', '/'), File.ReadAllBytes(file)))
                    .ToArray();
    }

    private static string GetGlobalUsings(SnapshotTestFramework testFramework)
    {
        var globalUsings = new List<string>()
        {
            "global using System;",
            "global using System.Collections.Generic;",
            "global using System.IO;",
            "global using System.Runtime.CompilerServices;",
            "global using System.Text;",
            "global using Meziantou.Framework.SnapshotTesting;",
        };

        var frameworkGlobalUsing = testFramework switch
        {
            SnapshotTestFramework.Xunit or SnapshotTestFramework.XunitV3 => "global using Xunit;",
            SnapshotTestFramework.MSTest => "global using Microsoft.VisualStudio.TestTools.UnitTesting;",
            SnapshotTestFramework.NUnit => "global using NUnit.Framework;",
            _ => null,
        };

        if (frameworkGlobalUsing is not null)
        {
            globalUsings.Add(frameworkGlobalUsing);
        }

        return string.Join(Environment.NewLine, globalUsings);
    }

    private static string GetTargetFramework()
    {
#if NET472
        return "net472";
#elif NET48
        return "net48";
#elif NET8_0
        return "net8.0";
#elif NET9_0
        return "net9.0";
#elif NET10_0
        return "net10.0";
#else
        throw new NotSupportedException("Unsupported target framework");
#endif
    }

    private static string GetPackageReferences(SnapshotTestFramework testFramework)
    {
        string[] references = testFramework switch
        {
            SnapshotTestFramework.Xunit =>
            [
                """<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />""",
                """<PackageReference Include="xunit" Version="2.9.3" />""",
                """<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />""",
            ],
            SnapshotTestFramework.XunitV3 =>
            [
                """<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />""",
                """<PackageReference Include="xunit.v3" Version="3.2.2" />""",
                """<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />""",
            ],
            SnapshotTestFramework.MSTest =>
            [
                """<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />""",
                """<PackageReference Include="MSTest.TestFramework" Version="4.2.1" />""",
                """<PackageReference Include="MSTest.TestAdapter" Version="4.2.1" />""",
            ],
            SnapshotTestFramework.NUnit =>
            [
                """<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />""",
                """<PackageReference Include="NUnit" Version="4.5.1" />""",
                """<PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />""",
            ],
            SnapshotTestFramework.TUnit =>
            [
                """<PackageReference Include="TUnit" Version="1.35.2" />""",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(testFramework), testFramework, null),
        };

        return string.Join(Environment.NewLine, references);
    }

    private static async Task<DotNetExecutionResult> ExecuteDotNet(FullPath workingDirectory, string dotnetPath, IReadOnlyList<string> arguments, int? expectedExitCode = null)
    {
        var process = ProcessWrapper.Create(dotnetPath)
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory)
            .WithValidation(ProcessValidationMode.None)
            .WithEnvironmentVariables(env =>
            {
                env.Remove("CI");
                foreach (var entry in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>())
                {
                    var key = (string)entry.Key;
                    if (key == "GITHUB_WORKSPACE")
                        continue;

                    if (key.StartsWith("GITHUB", StringComparison.Ordinal))
                    {
                        env.Remove(key);
                    }
                }

                env.Set("DiffEngine_Disabled", "true");
                env.Set("MF_CurrentDirectory", Environment.CurrentDirectory);
            })
            .ExecuteBufferedAsync();

        var result = await process;
        var output = new StringBuilder();
        foreach (var line in result.Output)
        {
            output.AppendLine(line.Text);
        }

        if (expectedExitCode.HasValue)
        {
            Assert.Equal(expectedExitCode.Value, result.ExitCode);
        }

        return new DotNetExecutionResult(result.ExitCode, output.ToString());
    }

    private readonly record struct DotNetExecutionResult(int ExitCode, string Output);
    private sealed record SnapshotFile(string RelativePath, byte[] Content)
    {
        public string ContentAsString => Encoding.UTF8.GetString(Content);
    }
}
