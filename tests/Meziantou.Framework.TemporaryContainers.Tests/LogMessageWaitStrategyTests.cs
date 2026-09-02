using System.Text.RegularExpressions;
using Meziantou.Framework.TemporaryContainers.Strategies;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class LogMessageWaitStrategyTests
{
    private static readonly ContainerInfo RunningContainer = new() { Id = "id", Name = "name", State = ContainerState.Running, Status = "running" };
    private static readonly ContainerInfo ExitedContainer = new() { Id = "id", Name = "name", State = ContainerState.Exited, Status = "exited", ExitCode = 3 };

    [Fact]
    public async Task WaitAsync_ReattachesWhenTheLogStreamEndsWhileTheContainerIsRunning()
    {
        // The runtimes end the log stream on their own while the container is starting: 'docker logs -f' exits with
        // code 0 and the 'follow' response body ends, both without a single line. Giving up there fails a container
        // that is perfectly healthy.
        var attachCount = 0;
        var strategy = CreateStrategy("SERVER READY", occurrences: 1);

        await strategy.WaitCoreAsync(
            _ =>
            {
                attachCount++;
                return attachCount < 3 ? CreateLogsAsync() : CreateLogsAsync("SERVER READY");
            },
            () => Task.FromResult<ContainerInfo?>(RunningContainer),
            XunitCancellationToken);

        Assert.Equal(3, attachCount);
    }

    [Fact]
    public async Task WaitAsync_CountsTheOccurrencesOfEachAttachFromScratch()
    {
        // Attaching to the logs replays them from the beginning, so counting the matches of a new attach on top of
        // the previous ones would report a container as ready after a single occurrence seen twice.
        var attachCount = 0;
        var strategy = CreateStrategy("SERVER READY", occurrences: 2);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await strategy.WaitCoreAsync(
            _ =>
            {
                attachCount++;
                return CreateLogsAsync("SERVER READY");
            },
            () => Task.FromResult<ContainerInfo?>(attachCount < 2 ? RunningContainer : ExitedContainer),
            XunitCancellationToken));

        Assert.Equal(2, attachCount);
        Assert.Contains("matched 1 time(s)", exception.Message);
    }

    [Fact]
    public async Task WaitAsync_ReportsWhatTheContainerExitedWithAndPrinted()
    {
        var strategy = CreateStrategy("SERVER READY", occurrences: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await strategy.WaitCoreAsync(
            _ => CreateLogsAsync("the entrypoint gave up"),
            () => Task.FromResult<ContainerInfo?>(ExitedContainer),
            XunitCancellationToken));

        Assert.Contains("matched 0 time(s)", exception.Message);
        Assert.Contains("Exited", exception.Message);
        Assert.Contains("exit code 3", exception.Message);
        Assert.Contains("the entrypoint gave up", exception.Message);
    }

    [Fact]
    public async Task WaitAsync_KeepsWaitingUntilTheWaitIsCancelled()
    {
        // The startup timeout is what bounds the wait of a container that runs but never prints the message.
        var attachCount = 0;
        var strategy = CreateStrategy("SERVER READY", occurrences: 1);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await strategy.WaitCoreAsync(
            _ =>
            {
                attachCount++;
                return CreateLogsAsync();
            },
            () => Task.FromResult<ContainerInfo?>(RunningContainer),
            cts.Token));

        Assert.True(attachCount > 1, $"The log stream was attached {attachCount} time(s).");
    }

    [Fact]
    public async Task WaitAsync_ReportsAContainerThatCannotBeInspected()
    {
        var strategy = CreateStrategy("SERVER READY", occurrences: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await strategy.WaitCoreAsync(
            _ => CreateLogsAsync(),
            () => Task.FromResult<ContainerInfo?>(null),
            XunitCancellationToken));

        Assert.Contains("The container did not write anything to its log streams.", exception.Message);
    }

    private static LogMessageWaitStrategy CreateStrategy(string substring, int occurrences)
        => new(new Regex(Regex.Escape(substring), RegexOptions.None, TimeSpan.FromSeconds(1)), occurrences);

    private static async IAsyncEnumerable<LogEntry> CreateLogsAsync(params string[] messages)
    {
        foreach (var message in messages)
        {
            await Task.Yield();
            yield return new LogEntry(LogStream.Stdout, message, Timestamp: null);
        }
    }
}
