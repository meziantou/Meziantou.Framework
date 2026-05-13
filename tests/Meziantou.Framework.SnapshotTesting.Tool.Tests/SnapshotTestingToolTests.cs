namespace Meziantou.Framework.SnapshotTesting.Tool.Tests;

public sealed class SnapshotTestingToolTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task Approve_OverwritesVerifiedFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var actualPath = await temp.CreateTextFileAsync("__snapshots__/sample.actual.txt", "new-value", XunitCancellationToken);
        var verifiedPath = await temp.CreateTextFileAsync("__snapshots__/sample.verified.txt", "old-value", XunitCancellationToken);

        var result = await RunTool(["approve", "--folder", temp.FullPath]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(actualPath));
        Assert.Equal("new-value", await File.ReadAllTextAsync(verifiedPath));
    }

    [Fact]
    public async Task Approve_RecursesByDefault()
    {
        await using var temp = TemporaryDirectory.Create();
        var nestedActualPath = await temp.CreateTextFileAsync("nested/__snapshots__/sample.actual.txt", "nested-value", XunitCancellationToken);

        var result = await RunTool(["approve", "--folder", temp.FullPath]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(nestedActualPath));
        Assert.Equal("nested-value", await File.ReadAllTextAsync(temp.GetFullPath("nested/__snapshots__/sample.verified.txt")));
    }

    [Fact]
    public async Task Approve_DoesNotRecurse_WhenDisabled()
    {
        await using var temp = TemporaryDirectory.Create();
        var rootActualPath = await temp.CreateTextFileAsync("__snapshots__/root.actual.txt", "root-value", XunitCancellationToken);
        var nestedActualPath = await temp.CreateTextFileAsync("nested/__snapshots__/nested.actual.txt", "nested-value", XunitCancellationToken);

        var result = await RunTool(["approve", "--folder", temp.FullPath, "--recurse", "false"]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(rootActualPath));
        Assert.True(File.Exists(nestedActualPath));
        Assert.Equal("root-value", await File.ReadAllTextAsync(temp.GetFullPath("__snapshots__/root.verified.txt")));
    }

    [Fact]
    public async Task Approve_InteractiveMode_AllowsRejectingSnapshots()
    {
        await using var temp = TemporaryDirectory.Create();
        var firstActualPath = await temp.CreateTextFileAsync("__snapshots__/a.actual.txt", "value-a", XunitCancellationToken);
        var secondActualPath = await temp.CreateTextFileAsync("__snapshots__/b.actual.txt", "value-b", XunitCancellationToken);

        var result = await RunTool(
            ["approve", "--folder", temp.FullPath, "--interactive"],
            input: "y\nn\n");

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(firstActualPath));
        Assert.True(File.Exists(secondActualPath));
        Assert.Equal("value-a", await File.ReadAllTextAsync(temp.GetFullPath("__snapshots__/a.verified.txt")));
        Assert.False(File.Exists(temp.GetFullPath("__snapshots__/b.verified.txt")));
    }

    private async Task<(int ExitCode, string Output, string Error)> RunTool(string[] args, string? input = null)
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(args, console.ConfigureConsole, input is null ? null : new StringReader(input));

        return (exitCode, console.Output, console.Error);
    }
}
