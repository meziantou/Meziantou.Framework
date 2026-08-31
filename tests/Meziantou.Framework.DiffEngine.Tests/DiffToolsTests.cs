namespace Meziantou.Framework.DiffEngine.Tests;

public sealed class DiffToolsTests
{
    [Fact]
    public void TryFindByName_UsesEnvironmentVariableFilePath()
    {
        using var temp = TemporaryDirectory.Create();
        var executable = temp.CreateTextFile(GetVisualStudioCodeExecutableName(), "");
        using var scope = new EnvironmentVariableScope("DiffEngine_VisualStudioCode", executable);

        Assert.True(DiffTools.TryFindByName(DiffTool.VisualStudioCode, out var tool));
        Assert.NotNull(tool);
        AssertPathEqual(Path.GetFullPath(executable), tool.ExePath);
    }

    [Fact]
    public void TryFindByName_UsesEnvironmentVariableDirectoryPath()
    {
        using var temp = TemporaryDirectory.Create();
        var executable = temp.CreateTextFile(GetVisualStudioCodeExecutableName(), "");
        using var scope = new EnvironmentVariableScope("DiffEngine_VisualStudioCode", temp.FullPath);

        Assert.True(DiffTools.TryFindByName(DiffTool.VisualStudioCode, out var tool));
        Assert.NotNull(tool);
        AssertPathEqual(Path.GetFullPath(executable), tool.ExePath);
    }

    [Fact]
    public void TryFindByExtension_ReturnsBinaryTool()
    {
        using var temp = TemporaryDirectory.Create();
        _ = temp.CreateTextFile(GetVisualStudioCodeExecutableName(), "");
        using var scope = new EnvironmentVariableScope("DiffEngine_VisualStudioCode", temp.FullPath);
        using var pathScope = new EnvironmentVariableScope("PATH", temp.FullPath);

        Assert.True(DiffTools.TryFindByExtension(".bin", out var tool));
        Assert.NotNull(tool);
        Assert.Equal(DiffTool.VisualStudioCode, tool.Tool);
    }

    [Fact]
    public void TryFindByExtension_ReturnsTextTool()
    {
        using var temp = TemporaryDirectory.Create();
        _ = temp.CreateTextFile(GetVisualStudioCodeExecutableName(), "");
        using var scope = new EnvironmentVariableScope("DiffEngine_VisualStudioCode", temp.FullPath);
        using var pathScope = new EnvironmentVariableScope("PATH", temp.FullPath);

        Assert.True(DiffTools.TryFindByExtension(".txt", out var tool));
        Assert.NotNull(tool);
        Assert.Equal(DiffTool.VisualStudioCode, tool.Tool);
    }

    [Fact]
    public void GetArguments_HonorsTargetPosition()
    {
        using var temp = TemporaryDirectory.Create();
        _ = temp.CreateTextFile(GetVisualStudioCodeExecutableName(), "");
        using var diffToolScope = new EnvironmentVariableScope("DiffEngine_VisualStudioCode", temp.FullPath);
        using var targetPositionScope = new EnvironmentVariableScope("DiffEngine_TargetOnLeft", null);

        Assert.True(DiffTools.TryFindByName(DiffTool.VisualStudioCode, out var tool));
        Assert.NotNull(tool);
        Assert.Equal("--diff received.txt verified.txt", tool.GetArguments("received.txt", "verified.txt"));

        using var targetOnLeftScope = new EnvironmentVariableScope("DiffEngine_TargetOnLeft", "true");
        Assert.Equal("--diff verified.txt received.txt", tool.GetArguments("received.txt", "verified.txt"));
    }

    // Paths with a space, so the quoting branch of CommandLineBuilder is what the table shows.
    private const string TempFile = "/tmp/my snapshots/received.txt";
    private const string TargetFile = "/tmp/my snapshots/verified.txt";

    private const string StandardRight = "\"/tmp/my snapshots/received.txt\" \"/tmp/my snapshots/verified.txt\"";
    private const string StandardLeft = "\"/tmp/my snapshots/verified.txt\" \"/tmp/my snapshots/received.txt\"";
    private const string RiderRight = "diff \"/tmp/my snapshots/received.txt\" \"/tmp/my snapshots/verified.txt\"";
    private const string RiderLeft = "diff \"/tmp/my snapshots/verified.txt\" \"/tmp/my snapshots/received.txt\"";
    private const string VimRight = "-d \"/tmp/my snapshots/received.txt\" \"/tmp/my snapshots/verified.txt\"";
    private const string VimLeft = "-d \"/tmp/my snapshots/verified.txt\" \"/tmp/my snapshots/received.txt\"";
    private const string VisualStudioCodeRight = "--diff \"/tmp/my snapshots/received.txt\" \"/tmp/my snapshots/verified.txt\"";
    private const string VisualStudioCodeLeft = "--diff \"/tmp/my snapshots/verified.txt\" \"/tmp/my snapshots/received.txt\"";
    private const string VisualStudioRight = "/diff \"/tmp/my snapshots/received.txt\" \"/tmp/my snapshots/verified.txt\" received.txt verified.txt";
    private const string VisualStudioLeft = "/diff \"/tmp/my snapshots/verified.txt\" \"/tmp/my snapshots/received.txt\" verified.txt received.txt";
    private const string WinMergeRight = "/u /wl /e \"/tmp/my snapshots/received.txt\" \"/tmp/my snapshots/verified.txt\" /dl received.txt /dr verified.txt /cfg Backup/EnableFile=0";
    private const string WinMergeLeft = "/u /wr /e \"/tmp/my snapshots/verified.txt\" \"/tmp/my snapshots/received.txt\" /dl verified.txt /dr received.txt /cfg Backup/EnableFile=0";

    [Theory]
    [InlineData(DiffTool.MsWordDiff, StandardRight, StandardLeft)]
    [InlineData(DiffTool.MsExcelDiff, StandardRight, StandardLeft)]
    [InlineData(DiffTool.BeyondCompare, StandardRight, StandardLeft)]
    [InlineData(DiffTool.P4Merge, StandardRight, StandardLeft)]
    [InlineData(DiffTool.Kaleidoscope, StandardRight, StandardLeft)]
    [InlineData(DiffTool.DeltaWalker, StandardRight, StandardLeft)]
    [InlineData(DiffTool.WinMerge, WinMergeRight, WinMergeLeft)]
    [InlineData(DiffTool.TortoiseMerge, StandardRight, StandardLeft)]
    [InlineData(DiffTool.TortoiseGitMerge, StandardRight, StandardLeft)]
    [InlineData(DiffTool.TortoiseGitIDiff, StandardRight, StandardLeft)]
    [InlineData(DiffTool.TortoiseIDiff, StandardRight, StandardLeft)]
    [InlineData(DiffTool.KDiff3, StandardRight, StandardLeft)]
    [InlineData(DiffTool.TkDiff, StandardRight, StandardLeft)]
    [InlineData(DiffTool.Guiffy, StandardRight, StandardLeft)]
    [InlineData(DiffTool.ExamDiff, StandardRight, StandardLeft)]
    [InlineData(DiffTool.Diffinity, StandardRight, StandardLeft)]
    [InlineData(DiffTool.Rider, RiderRight, RiderLeft)]
    [InlineData(DiffTool.Vim, VimRight, VimLeft)]
    [InlineData(DiffTool.Neovim, VimRight, VimLeft)]
    [InlineData(DiffTool.AraxisMerge, StandardRight, StandardLeft)]
    [InlineData(DiffTool.Meld, StandardRight, StandardLeft)]
    [InlineData(DiffTool.SublimeMerge, StandardRight, StandardLeft)]
    [InlineData(DiffTool.VisualStudioCode, VisualStudioCodeRight, VisualStudioCodeLeft)]
    [InlineData(DiffTool.VisualStudio, VisualStudioRight, VisualStudioLeft)]
    [InlineData(DiffTool.Cursor, VisualStudioCodeRight, VisualStudioCodeLeft)]
    public void GetArguments_ProducesTheExpectedCommandLine(DiffTool tool, string expectedRight, string expectedLeft)
    {
        var resolvedTool = new ResolvedTool(tool, "diff", DiffTools.GetLaunchArguments(tool), supportsText: true, []);

        using (new EnvironmentVariableScope("DiffEngine_TargetOnLeft", null))
        {
            Assert.Equal(expectedRight, resolvedTool.GetArguments(TempFile, TargetFile));
        }

        using (new EnvironmentVariableScope("DiffEngine_TargetOnLeft", "true"))
        {
            Assert.Equal(expectedLeft, resolvedTool.GetArguments(TempFile, TargetFile));
        }
    }

    [Fact]
    public void GetArguments_EscapesAQuoteInThePath()
    {
        var resolvedTool = new ResolvedTool(DiffTool.VisualStudioCode, "diff", DiffTools.GetLaunchArguments(DiffTool.VisualStudioCode), supportsText: true, []);
        using var scope = new EnvironmentVariableScope("DiffEngine_TargetOnLeft", null);

        // A quote is a legal file name character on Linux and macOS. Unescaped, everything after it would
        // reach the diff tool as extra arguments.
        var arguments = resolvedTool.GetArguments("/tmp/a\" --wait --extensionDevelopmentPath=/evil \"x.txt", "/tmp/verified.txt");

        Assert.Equal("--diff \"/tmp/a\\\" --wait --extensionDevelopmentPath=/evil \\\"x.txt\" /tmp/verified.txt", arguments);
    }

    [Fact]
    public void GetArguments_EscapesBackslashesBeforeTheClosingQuote()
    {
        var resolvedTool = new ResolvedTool(DiffTool.Meld, "diff", DiffTools.GetLaunchArguments(DiffTool.Meld), supportsText: true, []);
        using var scope = new EnvironmentVariableScope("DiffEngine_TargetOnLeft", null);

        // A trailing backslash would otherwise escape the quote that closes the argument.
        var arguments = resolvedTool.GetArguments("C:\\my dir\\", "C:\\other dir\\");

        Assert.Equal("\"C:\\my dir\\\\\" \"C:\\other dir\\\\\"", arguments);
    }

    [Fact]
    public void EveryDiffToolHasLaunchArguments()
    {
        foreach (var tool in Enum.GetValues<DiffTool>())
        {
            _ = DiffTools.GetLaunchArguments(tool);
        }
    }

    private static string GetVisualStudioCodeExecutableName()
    {
        return OperatingSystem.IsWindows() ? "code.cmd" : "code";
    }

    private static void AssertPathEqual(string expected, string actual)
    {
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        Assert.Equal(expected, actual, comparer);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
