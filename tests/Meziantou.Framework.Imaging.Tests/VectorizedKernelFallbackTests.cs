using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The vectorized kernels pick their path once, from the instruction sets of the processor: a test process only runs the
/// widest one, so the narrower paths and the scalar fallbacks of a kernel never run on a machine that has the wide ones
/// (the <c>Vector128</c> paths on an x64 runner with AVX2, the <c>Vector256</c> paths on one with AVX-512). These tests run
/// <see cref="VectorizedKernelTests"/> again in a child process with an instruction set turned off by the runtime setting
/// that does it. A setting that changes nothing on this machine is skipped. They only run on x64: on Arm64 the default run
/// already uses the <c>Vector128</c> paths, and the scalar fallbacks are the same code on both architectures.
/// </summary>
public sealed class VectorizedKernelFallbackTests
{
    public static TheoryData<string> Settings => ["DOTNET_EnableAVX512", "DOTNET_EnableAVX2", "DOTNET_EnableHWIntrinsic"];

    [Theory]
    [MemberData(nameof(Settings))]
    public async Task VectorizedKernelsMatchTheScalarReferencesWithAnInstructionSetTurnedOff(string setting)
    {
        if (RuntimeInformation.ProcessArchitecture is not Architecture.X64)
            global::Xunit.Assert.Skip("The narrower vector paths are only skipped by default on x64.");

        var changesSomething = setting switch
        {
            "DOTNET_EnableAVX512" => Vector512.IsHardwareAccelerated,
            "DOTNET_EnableAVX2" => Avx2.IsSupported,
            _ => Vector128.IsHardwareAccelerated,
        };
        if (!changesSomething)
            global::Xunit.Assert.Skip($"{setting}=0 turns off nothing on this processor.");

        if (Environment.GetEnvironmentVariable(setting) is "0")
            global::Xunit.Assert.Skip($"{setting}=0 is already set for this process.");

        var assemblyPath = typeof(VectorizedKernelTests).Assembly.Location;
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The path of the test process is unknown.");
        var startInfo = new ProcessStartInfo(processPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // The tests run in the apphost of the test assembly, or in the dotnet host when they are started with 'dotnet exec'
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add(assemblyPath);
        }

        var resultsDirectory = Directory.CreateTempSubdirectory("meziantou-imaging-fallback-");
        foreach (var argument in (string[])["--filter-class", typeof(VectorizedKernelTests).FullName!, "--results-directory", resultsDirectory.FullName, "--no-progress", "--no-ansi"])
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The settings of the test run of the parent (its connection to 'dotnet test', its reports) are not the child's
        foreach (var name in startInfo.Environment.Keys.Where(name => name.StartsWith("TESTINGPLATFORM_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            startInfo.Environment.Remove(name);
        }

        startInfo.Environment[setting] = "0";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The test process did not start.");
        var cancellationToken = TestContext.Current.CancellationToken;
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        finally
        {
            resultsDirectory.Delete(recursive: true);
        }

        var output = await outputTask + await errorTask;
        Assert.True(process.ExitCode is 0, $"The tests of {nameof(VectorizedKernelTests)} failed with {setting}=0 (exit code {process.ExitCode}):{Environment.NewLine}{output}");

        // The run must not be an empty one that a filter or a handshake failure ended early
        var total = Regex.Match(output, @"^\s*total:\s*(?<count>[0-9]+)", RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        Assert.True(total.Success, $"The test run printed no total:{Environment.NewLine}{output}");
        Assert.True(int.Parse(total.Groups["count"].Value, CultureInfo.InvariantCulture) > 100, $"Too few tests ran with {setting}=0:{Environment.NewLine}{output}");
    }
}
