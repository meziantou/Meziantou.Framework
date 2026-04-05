using System.Diagnostics;
using System.Text.Json;
using TestUtilities;
using Xunit.Sdk;

namespace Meziantou.Framework.CommandLineTests;

public sealed class ArgumentPrinterClassFixture
{
    private static readonly SemaphoreSlim BuildSemaphore = new(1, 1);
    private static readonly FullPath BuildLockFilePath = FullPath.GetTempPath() / "meziantou-framework-commandline-tests-argumentsprinter.lock";
    private static bool s_isBuilt;

    private readonly string _dotnetPath;
    private readonly FullPath _projectPath;

    public ArgumentPrinterClassFixture()
    {
        var rootPath = FullPath.CurrentDirectory().FindRequiredGitRepositoryRoot();
        _projectPath = rootPath / "tests" / "ArgumentsPrinter" / "ArgumentsPrinter.csproj";
        _dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
    }

    public async ValueTask<string[]> RoundtripArguments(string arguments)
    {
        await EnsureBuiltAsync();

        var processArguments = $"run --no-build --project \"{_projectPath}\" -- {arguments}";
        var result = await ExecuteProcessAsync(_dotnetPath, processArguments);
        return ParseResult(result, _dotnetPath, processArguments);
    }

    public async ValueTask<string[]> RoundtripCmdArguments(string arguments)
    {
        await EnsureBuiltAsync();

        var batchPath = FullPath.GetTempPath() / $"{Guid.NewGuid():N}.cmd";
        var commandLine = $"\"{_dotnetPath}\" run --no-build --project \"{_projectPath}\" -- {arguments}";
        File.WriteAllText(batchPath, commandLine);

        var cmdArguments = $"/Q /C \"{batchPath}\"";
        try
        {
            var result = await ExecuteProcessAsync("cmd.exe", cmdArguments);
            return ParseResult(result, "cmd.exe", cmdArguments);
        }
        finally
        {
            File.Delete(batchPath);
        }
    }

    private async ValueTask EnsureBuiltAsync()
    {
        if (s_isBuilt)
            return;

        await BuildSemaphore.WaitAsync();
        try
        {
            if (s_isBuilt)
                return;

            await using var buildLock = await AcquireBuildLockAsync();
            if (s_isBuilt)
                return;

            var processArguments = $"build \"{_projectPath}\" --nologo";
            var result = await ExecuteProcessAsync(_dotnetPath, processArguments);
            EnsureSucceeded(result, _dotnetPath, processArguments, throwOnErrorOutput: false);
            s_isBuilt = true;
        }
        finally
        {
            BuildSemaphore.Release();
        }
    }

    private static string[] ParseResult(ProcessResult result, string fileName, string arguments)
    {
        EnsureSucceeded(result, fileName, arguments, throwOnErrorOutput: true);

        return JsonSerializer.Deserialize<string[]>(result.StandardOutput) ?? throw new XunitException("Cannot deserialize arguments as JSON");
    }

    private static void EnsureSucceeded(ProcessResult result, string fileName, string arguments, bool throwOnErrorOutput)
    {
        if (result.ExitCode != 0)
        {
            throw new XunitException($"Command failed with exit code {result.ExitCode}: '{fileName}' '{arguments}'\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
        }

        if (throwOnErrorOutput && !string.IsNullOrEmpty(result.StandardError))
        {
            throw new XunitException($"Command wrote to stderr: '{fileName}' '{arguments}'\nSTDERR:\n{result.StandardError}");
        }
    }

    private static async ValueTask<ProcessResult> ExecuteProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // https://github.com/Microsoft/vstest/issues/1263
        psi.EnvironmentVariables["COR_ENABLE_PROFILING"] = "0";

        using var process = Process.Start(psi) ?? throw new XunitException($"Cannot start process '{fileName}'");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(
            StandardOutput: await stdoutTask,
            StandardError: await stderrTask,
            ExitCode: process.ExitCode);
    }

    private static async ValueTask<FileStream> AcquireBuildLockAsync()
    {
        while (true)
        {
            try
            {
                return new FileStream(BuildLockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
    }

    private sealed record ProcessResult(string StandardOutput, string StandardError, int ExitCode);
}
