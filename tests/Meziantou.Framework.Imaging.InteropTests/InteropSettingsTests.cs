using System.ComponentModel;
using System.Diagnostics;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Xunit;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>Tool-free checks of the interop infrastructure, so the project always runs at least one test.</summary>
public sealed class InteropSettingsTests
{
    [Fact]
    public void ArtifactsDirectoryIsCreated()
    {
        var directory = InteropSettings.GetArtifactsDirectory(nameof(ArtifactsDirectoryIsCreated));
        Assert.True(Directory.Exists(directory));
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public async Task ProcessRunnerReturnsTheExitCodeTheRawOutputAndTheErrorText()
    {
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", @"printf '\000\377out'; printf 'err' >&2; exit 3"], XunitCancellationToken);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal<byte>([0, 255, (byte)'o', (byte)'u', (byte)'t'], result.StandardOutput.ToArray());
        Assert.Equal("err", result.StandardError);
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public async Task ProcessRunnerPassesTheArgumentsWithoutShellInterpretation()
    {
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", @"printf '%s|' ""$@""", "sh", "a b", "", "\u00E9\"'$HOME*"], XunitCancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("a b||\u00E9\"'$HOME*|", result.StandardOutputText);
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public async Task ProcessRunnerGivesAnEmptyStandardInputAndTheEnvironment()
    {
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", @"cat; printf '%s' ""$PATH"""], XunitCancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), result.StandardOutputText);
    }

    /// <summary>Each stream holds more than a pipe does: both must be read while the process runs.</summary>
    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public async Task ProcessRunnerReadsBothStreamsWhileTheProcessRuns()
    {
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", "head -c 1000000 /dev/zero; head -c 1000000 /dev/zero >&2"], XunitCancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.HasCount(1_000_000, result.StandardOutput.Span);
        Assert.HasCount(1_000_000, result.StandardError);
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public async Task ProcessRunnerReportsATerminatingSignalAsExitCode()
    {
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", "kill -9 $$"], XunitCancellationToken);
        Assert.Equal(128 + 9, result.ExitCode);
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public async Task ProcessRunnerKeepsConcurrentProcessesApart()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(index => ProcessRunner.RunAsync("/bin/sh", ["-c", @"printf '%s' ""$0""; printf '%s' ""$0"" >&2", index.ToString(CultureInfo.InvariantCulture)], XunitCancellationToken)));
        for (var index = 0; index < results.Length; index++)
        {
            Assert.Equal(0, results[index].ExitCode);
            Assert.Equal(index.ToString(CultureInfo.InvariantCulture), results[index].StandardOutputText);
            Assert.Equal(index.ToString(CultureInfo.InvariantCulture), results[index].StandardError);
        }
    }

    [Fact]
    public async Task ProcessRunnerFailsWhenTheExecutableDoesNotExist()
    {
        var path = FullPath.FromPath(AppContext.BaseDirectory) / "missing-tool";
        await Assert.Throws<Win32Exception>(() => ProcessRunner.RunAsync(path, [], XunitCancellationToken));
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public async Task ProcessRunnerKillsTheProcessAndItsChildrenOnCancellation()
    {
        // The shell starts a child, publishes its identifier and waits for it
        var childIdPath = InteropSettings.GetArtifactsDirectory(nameof(ProcessRunnerKillsTheProcessAndItsChildrenOnCancellation)) / (Guid.NewGuid().ToString("N") + ".pid");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        var run = ProcessRunner.RunAsync("/bin/sh", ["-c", @"sleep 120 & echo $! > ""$0.tmp"" && mv ""$0.tmp"" ""$0""; wait", childIdPath], cancellation.Token);
        var stopwatch = Stopwatch.StartNew();
        while (!File.Exists(childIdPath))
        {
            Assert.False(run.IsCompleted, "The shell exited before it started its child.");
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(1), "The shell did not start its child.");
            await Task.Delay(20, XunitCancellationToken);
        }

        var childId = int.Parse(await File.ReadAllTextAsync(childIdPath, XunitCancellationToken), CultureInfo.InvariantCulture);
        Assert.True(IsRunning(childId));

        await cancellation.CancelAsync();
        await Assert.ThrowsAny<OperationCanceledException>(() => run);
        while (IsRunning(childId))
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(1), "The child of the shell is still running.");
            await Task.Delay(20, XunitCancellationToken);
        }

        static bool IsRunning(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
