using System.Diagnostics;
using System.Security;
using System.Text;

namespace Meziantou.Framework.Geo.Maps.Tool;

/// <summary>Builds the packages of the projects that <see cref="MapProjectWriter"/> writes, with the .NET SDK.</summary>
internal static class DotNetCli
{
    /// <summary>Packs projects in one build, so that MSBuild builds them in parallel.</summary>
    /// <param name="projects">The project files.</param>
    /// <param name="directory">The folder of the projects, where the solution is written.</param>
    /// <param name="cancellationToken">The token to stop the build.</param>
    /// <returns>The exit code of the build and its output.</returns>
    public static async Task<(int ExitCode, string Output)> PackAsync(IReadOnlyList<FullPath> projects, FullPath directory, CancellationToken cancellationToken)
    {
        var solution = directory / "packages.slnx";
        var content = new StringBuilder("<Solution>\n");
        foreach (var project in projects)
        {
            content.Append("  <Project Path=\"").Append(SecurityElement.Escape(project.MakePathRelativeTo(directory).Replace('\\', '/'))).Append("\" />\n");
        }

        content.Append("</Solution>\n");
        await File.WriteAllTextAsync(solution, content.ToString(), cancellationToken).ConfigureAwait(false);

        var startInfo = new ProcessStartInfo(GetDotNetPath())
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("pack");
        startInfo.ArgumentList.Add(solution);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-consoleLoggerParameters:NoSummary");

        // The variables of the MSBuild that may run this tool, such as a test run, must not select its SDK or its tasks.
        // A reused MSBuild node would keep the output open, and the build would not end
        foreach (var name in startInfo.Environment.Keys.Where(IsMSBuildVariable).ToList())
        {
            startInfo.Environment.Remove(name);
        }

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The .NET SDK could not be started.");
        var output = new StringBuilder();
        var standardOutput = CopyAsync(process.StandardOutput, output);
        var standardError = CopyAsync(process.StandardError, output);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false);
        return (process.ExitCode, output.ToString());

        static async Task CopyAsync(StreamReader reader, StringBuilder output)
        {
            while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                lock (output)
                {
                    output.Append(line).Append('\n');
                }
            }
        }
    }

    private static bool IsMSBuildVariable(string name) =>
        name.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("_MSBuild", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "MSBUILD_EXE_PATH", StringComparison.OrdinalIgnoreCase);

    // The dotnet of the SDK that runs the tool, else the one of DOTNET_ROOT, else the one of the PATH
    private static string GetDotNetPath()
    {
        var fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath && File.Exists(hostPath))
            return hostPath;

        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root && File.Exists(Path.Combine(root, fileName)))
            return Path.Combine(root, fileName);

        return fileName;
    }
}
