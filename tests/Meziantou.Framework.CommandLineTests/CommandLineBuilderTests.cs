using System.Diagnostics;
using TestUtilities;
using Xunit.Sdk;

namespace Meziantou.Framework.CommandLineTests;

public class CommandLineBuilderTests
{
    private static readonly Lazy<FullPath> ArgumentPrinterPath = new(ResolveArgumentPrinterPath);
    private readonly ITestOutputHelper _testOutputHelper;

    public CommandLineBuilderTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public static TheoryData<string, string> GetArguments()
    {
        var result = new TheoryData<string, string>();
        Add(@"a");
        Add(@"arg 1");
        Add(@"\some\path with\spaces");
        Add(@"a\\b");
        Add(@"a\\\\b");
        Add(@"""a");
        Add(@"a|b");
        Add(@"ab|");
        Add(@"|ab");
        Add(@"^ab");
        Add(@"a^b");
        Add(@"ab^");
        Add(@"malicious argument"" & whoami");
        Add(@"""malicious-argument\^""^&whoami""");
        return result;

        void Add(string value) => result.Add(value, value);
    }

    [Theory]
    [MemberData(nameof(GetArguments))]
    public void WindowsQuotedArgument_Test(string value, string expected)
    {
        var args = CommandLineBuilder.WindowsQuotedArgument(value);
        var path = GetArgumentPrinterPath();

        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        ValidateArguments(dotnetPath, "\"" + path + "\" " + args, [expected]);
    }

    [Theory, RunIf(FactOperatingSystem.Windows)]
    [MemberData(nameof(GetArguments))]
    public void WindowsCmdArgument_Test(string value, string expected)
    {
        var args = CommandLineBuilder.WindowsCmdArgument(value);
        var batPath = FullPath.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cmd");

        var path = GetArgumentPrinterPath();
        var dotnetPath = ExecutableFinder.GetFullExecutablePath("dotnet");
        var fileContent = $"\"{dotnetPath}\" \"{path}\" {args}";
        File.WriteAllText(batPath, fileContent);

        var cmdArguments = "/Q /C \"" + batPath + "\"";

        _testOutputHelper.WriteLine($"Executing 'cmd.exe' '{cmdArguments}' with batch content:\n{fileContent}");
        ValidateArguments("cmd.exe", cmdArguments, [expected]);
    }

    private FullPath GetArgumentPrinterPath()
    {
        var path = ArgumentPrinterPath.Value;
        _testOutputHelper.WriteLine($"Use ArgumentsPrinter located at '{path}'");
        return path;
    }

    private static FullPath ResolveArgumentPrinterPath()
    {
        var rootPath = FullPath.CurrentDirectory() / ".." / ".." / ".." / "..";
        var outputPath = rootPath / "artifacts" / "bin" / "ArgumentsPrinter";
        var testedPaths = new List<FullPath>();

        var path = TryFindArgumentPrinterPath(outputPath, testedPaths);
        if (path is not null)
        {
            return path.Value;
        }

        BuildArgumentsPrinter(rootPath);

        path = TryFindArgumentPrinterPath(outputPath, testedPaths);
        if (path is not null)
        {
            return path.Value;
        }

        var existingFiles = new List<string>();
        if (Directory.Exists(outputPath))
        {
            existingFiles.AddRange(Directory.GetFiles(outputPath, "*", SearchOption.AllDirectories));
        }

        existingFiles.Sort(StringComparer.Ordinal);
        throw new XunitException($"File not found:\n{string.Join('\n', testedPaths)}\n. List of existing files:\n{string.Join('\n', existingFiles)}\nHave you built the ArgumentsPrinter project?");
    }

    private static FullPath? TryFindArgumentPrinterPath(FullPath outputPath, List<FullPath> testedPaths)
    {
        const string FileName = "ArgumentsPrinter.dll";
        foreach (var candidatePath in GetCandidatePaths(outputPath, FileName))
        {
            if (!testedPaths.Contains(candidatePath))
            {
                testedPaths.Add(candidatePath);
            }

            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private static IEnumerable<FullPath> GetCandidatePaths(FullPath outputPath, string fileName)
    {
        var currentOutputFolder = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        if (!string.IsNullOrEmpty(currentOutputFolder))
        {
            yield return outputPath / currentOutputFolder / fileName;
        }

        if (Directory.Exists(outputPath))
        {
            foreach (var directory in Directory.EnumerateDirectories(outputPath))
            {
                yield return FullPath.FromPath(directory) / fileName;
            }
        }
    }

    private static void BuildArgumentsPrinter(FullPath rootPath)
    {
        var currentOutputFolder = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var configuration = currentOutputFolder?.StartsWith("release_", StringComparison.OrdinalIgnoreCase) is true ? "Release" : "Debug";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(rootPath / "tests" / "ArgumentsPrinter" / "ArgumentsPrinter.csproj");
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(configuration);
        psi.ArgumentList.Add("--nologo");

        using var process = Process.Start(psi);
        if (process is null)
        {
            throw new XunitException("Cannot start dotnet process to build ArgumentsPrinter project");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode is not 0)
        {
            throw new XunitException($"Cannot build ArgumentsPrinter project\n{output}\n{error}");
        }
    }

    private void ValidateArguments(string fileName, string arguments, string[] expectedArguments)
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

        _testOutputHelper.WriteLine($"Executing '{fileName}' '{arguments}'");
        using var process = Process.Start(psi);
        process.WaitForExit();

        var errors = process.StandardError.ReadToEnd();
        Assert.True(string.IsNullOrEmpty(errors));

        var actualArguments = process.StandardOutput.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        _testOutputHelper.WriteLine("----------");
        foreach (var arg in actualArguments)
        {
            _testOutputHelper.WriteLine(arg);
        }

        Assert.Equal(0, process.ExitCode);
        Assert.Equal(expectedArguments, actualArguments);
    }
}
