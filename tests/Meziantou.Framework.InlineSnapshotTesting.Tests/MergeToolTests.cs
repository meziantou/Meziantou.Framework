using System.Diagnostics;
using Meziantou.Framework.DiffEngine;
using Meziantou.Framework.InlineSnapshotTesting.MergeTools;
using Meziantou.Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class MergeToolTests
{
    [Fact]
    public void ValidateEnumMembers()
    {
        var diffToolNames = Enum.GetNames<Meziantou.Framework.DiffEngine.DiffTool>();
        var inlineSnapshotPreferredDiffToolNames =
            typeof(MergeTool)
            .GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
            .Where(p => p.CanRead && typeof(MergeTool).IsAssignableFrom(p.PropertyType))
            .Select(p => p.Name);
        Assert.Empty(diffToolNames.Except(inlineSnapshotPreferredDiffToolNames, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("ping", "ping", "")]
    [InlineData("ping ", "ping", "")]
    [InlineData("ping a b", "ping", "a b")]
    [InlineData("\"ping\"", "ping", "")]
    [InlineData("\"ping\" ", "ping", "")]
    [InlineData("\"ping\" a b", "ping", "a b")]
    public void GitTool_ParseCommand(string value, string command, string arguments)
    {
        Assert.Equal((command, arguments), GitTool.ParseCommandFromConfiguration(value));
    }

    [Fact]
    public void GitTool_ExpandCommandWithoutShell_QuotesEachPlaceholder()
    {
        var (command, arguments) = GitTool.ExpandCommandWithoutShell("""
            "C:\Program Files\Tool\tool.exe" '$LOCAL' "$REMOTE" ${BASE} $MERGED $LOCALS /flag
            """,
            [
                new("LOCAL", @"C:\dir with spaces\local.cs"),
                new("REMOTE", @"C:\dir\remote.cs"),
                new("BASE", @"C:\a ""quoted"" dir\base.cs"),
                new("MERGED", @"C:\dir\merged.cs"),
            ]);

        Assert.Equal(@"C:\Program Files\Tool\tool.exe", command);
        Assert.Equal(@"""C:\dir with spaces\local.cs"" C:\dir\remote.cs ""C:\a \""quoted\"" dir\base.cs"" C:\dir\merged.cs $LOCALS /flag", arguments);
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void GitTool_CreateCommandStartInfo_PassesThePathsVerbatim()
    {
        using var directory = TemporaryDirectory.Create();
        var local = directory.GetFullPath("dir with spaces/local 'quoted' \"file\".cs");
        var remote = directory.GetFullPath("dir $HOME `pwd`/remote.cs");
        var output = directory.GetFullPath("output dir/merged.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var startInfo = GitTool.CreateCommandStartInfo("""printf '%s\n' "$LOCAL" "${REMOTE}" "$BASE" > "$MERGED" """, directory.FullPath,
        [
            new("LOCAL", local),
            new("REMOTE", remote),
            new("BASE", local),
            new("MERGED", output),
        ]);

        using (var process = Process.Start(startInfo)!)
        {
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        string[] expectedLines = [local.Value, remote.Value, local.Value];
        Assert.Equal(expectedLines, File.ReadAllLines(output));
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void GitMergeTool_RunsTheConfiguredCommandAndDeletesTheCopyOfTheSourceFile()
    {
        var gitPath = ExecutableFinder.GetFullExecutablePath("git");
        global::Xunit.Assert.SkipWhen(gitPath is null, "git is not installed.");

        using var directory = TemporaryDirectory.Create();
        var repository = directory.GetFullPath("repo with spaces");
        Directory.CreateDirectory(repository);
        RunGit(gitPath, repository, "init");
        RunGit(gitPath, repository, "config", "merge.tool", "my tool");
        RunGit(gitPath, repository, "config", "mergetool.my tool.cmd", """cp "$REMOTE" "$MERGED" && printf '%s' "$LOCAL" > "$MERGED.local" && cmp -s "$LOCAL" "$BASE" """);

        var sourcePath = repository / "Sample.cs";
        var updatedPath = repository / "Updated.cs";
        File.WriteAllText(sourcePath, "recorded");
        File.WriteAllText(updatedPath, "sample");

        using (var result = MergeTool.GitMergeTool.Start(sourcePath, updatedPath))
        {
            Assert.NotNull(result);
            result.WaitForExit();
            Assert.Equal(0, ((ProcessMergeToolResult)result).Process.ExitCode);
        }

        Assert.Equal("sample", File.ReadAllText(sourcePath));
        var copyPath = File.ReadAllText(sourcePath + ".local");
        Assert.NotEqual(sourcePath.Value, copyPath);

        // The copy is deleted by the exit notification, which runs on the thread pool.
        var copyDirectory = Path.GetDirectoryName(copyPath)!;
        var stopwatch = Stopwatch.StartNew();
        while (Directory.Exists(copyDirectory) && stopwatch.Elapsed < TimeSpan.FromMinutes(2))
        {
            Thread.Sleep(50);
        }

        Assert.False(Directory.Exists(copyDirectory));
    }

    [Fact]
    public async Task ProcessMergeToolResult_RunsTheCleanupWhenReleasedBeforeTheProcessExits()
    {
        var cleanedUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = ProcessMergeToolResult.Start(CreateShellStartInfo(OperatingSystem.IsWindows() ? "ping -n 2 127.0.0.1 > NUL" : "sleep 1"), onExited: cleanedUp.SetResult);

        // The non-blocking merge tool strategy releases the result right after the tool starts.
        result.Dispose();

        await cleanedUp.Task.WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Launch_TriesTheNextMergeToolWhenOneFailsToStart()
    {
        var failures = new List<MergeToolLaunchFailure>();
        var failingTool = new ThrowingMergeTool();
        using var expectedResult = new CompletedMergeToolResult();

        var result = MergeTool.Launch([null, failingTool, new FixedMergeTool(expectedResult)], "current.cs", "new.cs", waitForMerge: false, failures);

        Assert.Same(expectedResult, result);
        var failure = Assert.Single(failures);
        Assert.Same(failingTool, failure.Tool);
        Assert.Equal("The merge tool is broken.", failure.Exception.Message);
    }

    [Fact]
    public void AutoDiffEngineTool_SkipsTerminalTools()
    {
        var selectedTool = AutoDiffEngineTool.SelectTool(".cs",
        [
            (DiffTool.Vim, true, []),
            (DiffTool.Neovim, true, []),
            (DiffTool.Meld, true, []),
        ]);

        Assert.Equal(DiffTool.Meld, selectedTool);
    }

    [Fact]
    public void DiffEngineTool_CreateStartInfo_EscapesTheArgumentsOfABatchFileForCmd()
    {
        var startInfo = DiffEngineTool.CreateStartInfo(@"C:\Tools\code.cmd", CommandLineBuilder.WindowsQuotedArguments("--diff", @"C:\src\R&D\a b.cs", @"C:\100%\b.cs"));

        Assert.Equal("cmd.exe", Path.GetFileName(startInfo.FileName));
        Assert.Equal("/d /e:on /v:off /s /c \"" + CommandLineBuilder.WindowsCmdArguments(@"C:\Tools\code.cmd", "--diff", @"C:\src\R&D\a b.cs", @"C:\100%\b.cs") + "\"", startInfo.Arguments);
    }

    private static void RunGit(string gitPath, string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo(gitPath)
        {
            WorkingDirectory = workingDirectory,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private static ProcessStartInfo CreateShellStartInfo(string command)
    {
        var startInfo = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe") : new ProcessStartInfo("/bin/sh");
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        startInfo.ArgumentList.Add(command);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        return startInfo;
    }

    private sealed class FixedMergeTool(MergeToolResult result) : MergeTool
    {
        public override MergeToolResult? Start(string currentFilePath, string newFilePath) => result;
    }

    private sealed class CompletedMergeToolResult : MergeToolResult
    {
        public override void Dispose()
        {
        }

        public override void WaitForExit()
        {
        }
    }

    private sealed class ThrowingMergeTool : MergeTool
    {
        public override MergeToolResult? Start(string currentFilePath, string newFilePath) => throw new InvalidOperationException("The merge tool is broken.");
    }
}
