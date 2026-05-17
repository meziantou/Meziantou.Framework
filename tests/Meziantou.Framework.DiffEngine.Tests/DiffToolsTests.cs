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
        Assert.Equal("--diff \"received.txt\" \"verified.txt\"", tool.GetArguments("received.txt", "verified.txt"));

        using var targetOnLeftScope = new EnvironmentVariableScope("DiffEngine_TargetOnLeft", "true");
        Assert.Equal("--diff \"verified.txt\" \"received.txt\"", tool.GetArguments("received.txt", "verified.txt"));
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
