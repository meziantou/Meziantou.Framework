#nullable enable

using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class SnapshotEndToEndTests
{
    [Fact]
    public async Task Validate_EndToEnd_CreatesReceivedFile()
    {
        await AssertSnapshot(
            """
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Runtime.CompilerServices;
            using System.Text;
            using Meziantou.Framework.SnapshotTesting;
            using Xunit;
            
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public void CreatesReceivedSnapshotFile()
                {
                    var expectedPath = GetExpectedPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
                    File.WriteAllText(expectedPath, "expected");
            
                    var settings = CreateSettings();
                    Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));
                }
            
                private static SnapshotSettings CreateSettings()
                {
                    var settings = new SnapshotSettings()
                    {
                        AutoDetectContinuousEnvironment = false,
                        SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
                        AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
                        FileNameStrategy = static _ => "snapshot_0.txt",
                    };
            
                    settings.SetSnapshotSerializer(SnapshotType.Default, new FixedValueSerializer("actual"));
                    return settings;
                }
            
                private static string GetExpectedPath([CallerFilePath] string? filePath = null)
                {
                    var sourceDirectory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Source directory not found.");
                    return Path.Combine(sourceDirectory, "__snapshots__", "snapshot_0.txt");
                }
            
                private sealed class FixedValueSerializer(string value) : ISnapshotSerializer
                {
                    public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value_)
                    {
                        return [new SnapshotData("txt", Encoding.UTF8.GetBytes(value))];
                    }
                }
            
                private sealed class FixedAssertionExceptionBuilder : AssertionExceptionBuilder
                {
                    public override Exception CreateException(string message) => new SnapshotAssertionException(message);
                }
            }
            """,
            expectedReceivedFileName: "snapshot_0.received.txt",
            expectedReceivedContent: "actual");
    }

    [Fact]
    public async Task Validate_EndToEnd_RetriesWhenReceivedFileIsLocked()
    {
        await AssertSnapshot(
            """
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Runtime.CompilerServices;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            using Meziantou.Framework.SnapshotTesting;
            using Xunit;
            
            public sealed class GeneratedSnapshotTests
            {
                [Fact]
                public async Task RetriesWhenReceivedFileIsLocked()
                {
                    var expectedPath = GetExpectedPath();
                    var receivedPath = GetReceivedPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
                    File.WriteAllText(expectedPath, "expected");
                    File.WriteAllText(receivedPath, "locked");
            
                    var settings = CreateSettings();
                    using var lockStream = new FileStream(receivedPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    var releaseTask = Task.Run(() =>
                    {
                        Thread.Sleep(250);
                        lockStream.Dispose();
                    });
            
                    try
                    {
                        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));
                    }
                    finally
                    {
                        await releaseTask;
                    }
            
                    Assert.Equal("actual", File.ReadAllText(receivedPath));
                }
            
                private static SnapshotSettings CreateSettings()
                {
                    var settings = new SnapshotSettings()
                    {
                        AutoDetectContinuousEnvironment = false,
                        SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
                        AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
                        FileNameStrategy = static _ => "snapshot_0.txt",
                    };
            
                    settings.SetSnapshotSerializer(SnapshotType.Default, new FixedValueSerializer("actual"));
                    return settings;
                }
            
                private static string GetExpectedPath([CallerFilePath] string? filePath = null)
                {
                    var sourceDirectory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Source directory not found.");
                    return Path.Combine(sourceDirectory, "__snapshots__", "snapshot_0.txt");
                }
            
                private static string GetReceivedPath([CallerFilePath] string? filePath = null)
                {
                    var sourceDirectory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Source directory not found.");
                    return Path.Combine(sourceDirectory, "__snapshots__", "snapshot_0.received.txt");
                }
            
                private sealed class FixedValueSerializer(string value) : ISnapshotSerializer
                {
                    public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value_)
                    {
                        return [new SnapshotData("txt", Encoding.UTF8.GetBytes(value))];
                    }
                }
            
                private sealed class FixedAssertionExceptionBuilder : AssertionExceptionBuilder
                {
                    public override Exception CreateException(string message) => new SnapshotAssertionException(message);
                }
            }
            """,
            expectedReceivedFileName: "snapshot_0.received.txt",
            expectedReceivedContent: "actual");
    }

    [SuppressMessage("Design", "MA0042:Do not use blocking calls in an async method", Justification = "Used to align behavior with integration test process execution.")]
    [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Synchronous process APIs are used with event-based output capture.")]
    private static async Task AssertSnapshot([StringSyntax("c#-test")] string source, string expectedReceivedFileName, string expectedReceivedContent)
    {
        await using var directory = TemporaryDirectory.Create();
        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        Assert.NotNull(dotnetPath);

        await ExecuteDotNet(directory.FullPath, dotnetPath, "new xunit --framework net8.0 --output . --force", expectedExitCode: 0);

        var projectFilePath = Directory.GetFiles(directory.FullPath, "*.csproj", SearchOption.TopDirectoryOnly).Single();
        var snapshotProjectPath = Path.Combine(GetRepositoryRoot(), "src", "Meziantou.Framework.SnapshotTesting", "Meziantou.Framework.SnapshotTesting.csproj");
        var projectFileContent = File.ReadAllText(projectFilePath);
        projectFileContent = projectFileContent.Replace(
            "</Project>",
            $$"""
                <ItemGroup>
                    <ProjectReference Include="{{snapshotProjectPath}}" />
                </ItemGroup>
                </Project>
                """,
            StringComparison.Ordinal);
        File.WriteAllText(projectFilePath, projectFileContent);

        var templateTestPath = directory.GetFullPath("UnitTest1.cs");
        if (File.Exists(templateTestPath))
        {
            File.Delete(templateTestPath);
        }

        File.WriteAllText(directory.GetFullPath("SnapshotIntegrationTests.cs"), source);

        await ExecuteDotNet(directory.FullPath, dotnetPath, "restore", expectedExitCode: 0);
        await ExecuteDotNet(directory.FullPath, dotnetPath, "build --no-restore", expectedExitCode: 0);
        var testResult = await ExecuteDotNet(directory.FullPath, dotnetPath, "test --no-build --nologo -v minimal", expectedExitCode: 0);

        var receivedPath = directory.GetFullPath(Path.Combine("__snapshots__", expectedReceivedFileName));
        Assert.True(File.Exists(receivedPath), testResult.Output);
        Assert.Equal(expectedReceivedContent, File.ReadAllText(receivedPath));
    }

    private static string GetRepositoryRoot([CallerFilePath] string? filePath = null)
    {
        var directory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Cannot resolve source directory.");
        return Path.GetFullPath(Path.Combine(directory, "..", ".."));
    }

    private static async Task<DotNetExecutionResult> ExecuteDotNet(string workingDirectory, string dotnetPath, string command, int? expectedExitCode = null)
    {
        var startInfo = new ProcessStartInfo(dotnetPath, command)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.EnvironmentVariables.Remove("CI");
        foreach (var entry in startInfo.EnvironmentVariables.Cast<DictionaryEntry>().ToArray())
        {
            var key = (string)entry.Key;
            if (key == "GITHUB_WORKSPACE")
                continue;

            if (key.StartsWith("GITHUB", StringComparison.Ordinal))
            {
                startInfo.EnvironmentVariables.Remove(key);
            }
        }

        startInfo.EnvironmentVariables["DiffEngine_Disabled"] = "true";
        startInfo.EnvironmentVariables["MF_CurrentDirectory"] = Environment.CurrentDirectory;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var outputBuilder = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        var output = outputBuilder.ToString();
        if (expectedExitCode.HasValue)
        {
            Assert.Equal(expectedExitCode.Value, process.ExitCode);
        }

        return new DotNetExecutionResult(process.ExitCode, output);
    }

    private readonly record struct DotNetExecutionResult(int ExitCode, string Output);
}
