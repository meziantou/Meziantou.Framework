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
            _ => Task.FromResult<ContainerInfo?>(RunningContainer),
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
            _ => Task.FromResult<ContainerInfo?>(attachCount < 2 ? RunningContainer : ExitedContainer),
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
            _ => Task.FromResult<ContainerInfo?>(ExitedContainer),
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

        // The wait is cancelled once it has re-attached a few times, rather than after a fixed delay: a busy thread pool
        // can delay the re-attachments well past any budget.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await strategy.WaitCoreAsync(
            _ =>
            {
                attachCount++;
                if (attachCount == 3)
                    cts.Cancel();

                return CreateLogsAsync();
            },
            _ => Task.FromResult<ContainerInfo?>(RunningContainer),
            cts.Token));

        Assert.Equal(3, attachCount);
    }

    [Fact]
    public async Task WaitAsync_ReportsAContainerThatCannotBeInspected()
    {
        var inspections = 0;
        var strategy = CreateStrategy("SERVER READY", occurrences: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await strategy.WaitCoreAsync(
            _ => CreateLogsAsync(),
            _ =>
            {
                inspections++;
                return Task.FromResult<ContainerInfo?>(null);
            },
            XunitCancellationToken));

        Assert.Equal(LogMessageWaitStrategy.MaxFailedInspections, inspections);
        Assert.Contains("The state of the container could not be read.", exception.Message);
        Assert.Contains("The container did not write anything to its log streams.", exception.Message);
    }

    [Fact]
    public async Task WaitAsync_KeepsWaitingWhenAnInspectFailsOnce()
    {
        // A busy runtime can fail to answer an inspect while the container is perfectly healthy. That says nothing about
        // the container, so the wait must not fail it.
        var attachCount = 0;
        var strategy = CreateStrategy("SERVER READY", occurrences: 1);

        await strategy.WaitCoreAsync(
            _ =>
            {
                attachCount++;
                return attachCount < 3 ? CreateLogsAsync() : CreateLogsAsync("SERVER READY");
            },
            _ => Task.FromResult<ContainerInfo?>(attachCount == 1 ? null : RunningContainer),
            XunitCancellationToken);

        Assert.Equal(3, attachCount);
    }

    [Fact]
    public async Task WaitAsync_ReattachesWhenTheLogStreamFaultsWhileTheContainerIsRunning()
    {
        // A runtime that drops the connection in the middle of a frame faults the stream instead of ending it.
        var attachCount = 0;
        var strategy = CreateStrategy("SERVER READY", occurrences: 1);

        await strategy.WaitCoreAsync(
            _ =>
            {
                attachCount++;
                return attachCount == 1 ? CreateFaultingLogsAsync(new EndOfStreamException("Unexpected end of stream.")) : CreateLogsAsync("SERVER READY");
            },
            _ => Task.FromResult<ContainerInfo?>(RunningContainer),
            XunitCancellationToken);

        Assert.Equal(2, attachCount);
    }

    [Fact]
    public async Task WaitAsync_PassesTheCancellationTokenToTheInspection()
    {
        var strategy = CreateStrategy("SERVER READY", occurrences: 1);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        CancellationToken observed = default;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await strategy.WaitCoreAsync(
            _ => CreateLogsAsync(),
            token =>
            {
                observed = token;
                return Task.FromResult<ContainerInfo?>(ExitedContainer);
            },
            cts.Token));

        Assert.Equal(cts.Token, observed);
    }

    private static LogMessageWaitStrategy CreateStrategy(string substring, int occurrences)
        => new(new Regex(Regex.Escape(substring), RegexOptions.None, TimeSpan.FromSeconds(1)), occurrences);

    private static async IAsyncEnumerable<LogEntry> CreateFaultingLogsAsync(Exception exception)
    {
        await Task.Yield();
        yield return new LogEntry(LogStream.Stdout, "starting", Timestamp: null);
        throw exception;
    }

    private static async IAsyncEnumerable<LogEntry> CreateLogsAsync(params string[] messages)
    {
        foreach (var message in messages)
        {
            await Task.Yield();
            yield return new LogEntry(LogStream.Stdout, message, Timestamp: null);
        }
    }
}
