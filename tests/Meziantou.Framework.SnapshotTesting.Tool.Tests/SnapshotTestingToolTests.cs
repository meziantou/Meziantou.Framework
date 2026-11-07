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

    [Theory]
    [InlineData("sample.actual.g.cs", "sample.verified.g.cs")]
    [InlineData("sample.actual.d.ts", "sample.verified.d.ts")]
    [InlineData("Parse.actual.value.actual.txt", "Parse.actual.value.verified.txt")]
    public async Task Approve_MapsTheActualFileToItsVerifiedFile(string actualFileName, string verifiedFileName)
    {
        await using var temp = TemporaryDirectory.Create();
        var actualPath = await temp.CreateTextFileAsync("__snapshots__/" + actualFileName, "new-value", XunitCancellationToken);

        var result = await RunTool(["approve", "--folder", temp.FullPath]);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(actualPath));
        Assert.Equal([verifiedFileName], [.. Directory.GetFiles(temp.GetFullPath("__snapshots__")).Select(path => Path.GetFileName(path))]);
        Assert.Equal("new-value", await File.ReadAllTextAsync(temp.GetFullPath("__snapshots__/" + verifiedFileName)));
    }

    [Fact]
    public async Task Approve_DoesNotRenameAVerifiedFileWhoseNameContainsTheActualMarker()
    {
        await using var temp = TemporaryDirectory.Create();
        var verifiedPath = await temp.CreateTextFileAsync("__snapshots__/Parse.actual.value.verified.txt", "committed", XunitCancellationToken);

        var result = await RunTool(["approve", "--folder", temp.FullPath]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Found 0 snapshot(s).", result.Output);
        Assert.Equal("committed", await File.ReadAllTextAsync(verifiedPath));
        Assert.Single(Directory.GetFiles(temp.GetFullPath("__snapshots__")));
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
