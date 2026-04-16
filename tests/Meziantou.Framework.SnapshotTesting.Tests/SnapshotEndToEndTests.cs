#nullable enable

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Meziantou.Framework;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class SnapshotEndToEndTests
{
    [Fact]
    public async Task Validate_EndToEnd_DotNetTestCreatesReceivedFile()
    {
        await using var directory = TemporaryDirectory.Create();
        var repositoryRoot = GetRepositoryRoot();
        var snapshotProjectPath = Path.Combine(repositoryRoot, "src", "Meziantou.Framework.SnapshotTesting", "Meziantou.Framework.SnapshotTesting.csproj");
        var createProjectResult = await ExecuteDotNet(directory.FullPath, "new xunit --framework net8.0 --output . --force");
        Assert.True(createProjectResult.ExitCode == 0, createProjectResult.Output);

        var projectFilePath = Directory.GetFiles(directory.FullPath, "*.csproj", SearchOption.TopDirectoryOnly).Single();
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

        var testFileContent = """
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
                public void CreatesReceivedSnapshotFile()
                {
                    var (settings, expectedPath, receivedPath) = CreateSettings();
                    File.WriteAllText(expectedPath, "expected");
            
                    Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));
            
                    Assert.True(File.Exists(receivedPath));
                    Assert.Equal("actual", File.ReadAllText(receivedPath));
                }
            
                [Fact]
                public void RetriesWhenReceivedFileIsLocked()
                {
                    var (settings, expectedPath, receivedPath) = CreateSettings();
                    File.WriteAllText(expectedPath, "expected");
                    File.WriteAllText(receivedPath, "locked");
            
                    var lockStream = new FileStream(receivedPath, FileMode.Open, FileAccess.Read, FileShare.None);
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
                        releaseTask.GetAwaiter().GetResult();
                    }
            
                    Assert.Equal("actual", File.ReadAllText(receivedPath));
                }
            
                private static (SnapshotSettings Settings, string ExpectedPath, string ReceivedPath) CreateSettings()
                {
                    var sourceFilePath = GetCurrentSourceFilePath();
                    var sourceDirectory = Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("Source directory not found.");
                    var expectedPath = Path.Combine(sourceDirectory, "__snapshots__", "snapshot_0.txt");
                    var receivedPath = Path.Combine(sourceDirectory, "__snapshots__", "snapshot_0.received.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
                    if (File.Exists(receivedPath))
                    {
                        File.Delete(receivedPath);
                    }
            
                    var settings = new SnapshotSettings()
                    {
                        AutoDetectContinuousEnvironment = false,
                        SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
                        AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
                        FileNameStrategy = static _ => "snapshot_0.txt",
                    };
            
                    settings.SetSnapshotSerializer(SnapshotType.Default, new FixedValueSerializer("actual"));
                    return (settings, expectedPath, receivedPath);
                }
            
                private static string GetCurrentSourceFilePath([CallerFilePath] string? filePath = null) => filePath ?? throw new InvalidOperationException("Caller file path not found.");
            
                private sealed class FixedValueSerializer(string value) : ISnapshotSerializer
                {
                    public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value_)
                    {
                        return new[] { new SnapshotData("txt", Encoding.UTF8.GetBytes(value)) };
                    }
                }
            
                private sealed class FixedAssertionExceptionBuilder : AssertionExceptionBuilder
                {
                    public override Exception CreateException(string message) => new SnapshotAssertionException(message);
                }
            }
            """;
        File.WriteAllText(directory.GetFullPath("SnapshotIntegrationTests.cs"), testFileContent);

        var result = await ExecuteDotNet(directory.FullPath, "test --nologo -v minimal");
        Assert.True(result.ExitCode == 0, result.Output);

        var receivedPath = directory.GetFullPath(Path.Combine("__snapshots__", "snapshot_0.received.txt"));
        Assert.True(File.Exists(receivedPath), result.Output);
        Assert.Equal("actual", File.ReadAllText(receivedPath));
    }

    private static async Task<DotNetExecutionResult> ExecuteDotNet(string workingDirectory, string arguments)
    {
        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        Assert.NotNull(dotnetPath);

        var startInfo = new ProcessStartInfo(dotnetPath, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.EnvironmentVariables["DiffEngine_Disabled"] = "true";

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var outputBuilder = new StringBuilder();
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                outputBuilder.AppendLine(eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                outputBuilder.AppendLine(eventArgs.Data);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return new DotNetExecutionResult(process.ExitCode, outputBuilder.ToString());
    }

    private static string GetRepositoryRoot([CallerFilePath] string? filePath = null)
    {
        var directory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Cannot resolve source directory.");
        return Path.GetFullPath(Path.Combine(directory, "..", ".."));
    }

    private readonly record struct DotNetExecutionResult(int ExitCode, string Output);
}
