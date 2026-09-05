using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class DockerApiRuntimeTests
{
    /// <summary>The Docker Engine API has its own volume endpoints, which no other test exercises. It is skipped when the daemon does not answer.</summary>
    [Fact]
    public async Task Volumes_CreateExistsAndDelete()
    {
        var runtime = new DockerApiRuntime();
        global::Xunit.Assert.SkipUnless(await runtime.IsSupportedAsync(XunitCancellationToken), "The Docker Engine API is not available on this system.");

        await using var volume = new VolumeDefinition { Runtime = runtime }.CreateVolume();
        volume.Definition.Labels.Add("owner", "meziantou");

        Assert.False(await volume.ExistsAsync(XunitCancellationToken));

        await volume.EnsureCreatedAsync(XunitCancellationToken);
        Assert.True(await volume.ExistsAsync(XunitCancellationToken));

        await volume.DeleteAsync(XunitCancellationToken);
        Assert.False(await volume.ExistsAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task ReadMultiplexedLogsAsync_SplitsFramesIntoLines()
    {
        var entries = await ReadAllAsync(
            (LogStream.Stdout, "first\nsecond\n"),
            (LogStream.Stderr, "boom\n"));

        Assert.Equal(["first", "second", "boom"], entries.Select(entry => entry.Message));
        Assert.Equal([LogStream.Stdout, LogStream.Stdout, LogStream.Stderr], entries.Select(entry => entry.Stream));
    }

    [Fact]
    public async Task ReadMultiplexedLogsAsync_JoinsALineSplitAcrossFrames()
    {
        var entries = await ReadAllAsync(
            (LogStream.Stdout, "SERVER "),
            (LogStream.Stdout, "READY\n"));

        Assert.Equal("SERVER READY", Assert.Single(entries).Message);
    }

    [Fact]
    public async Task ReadMultiplexedLogsAsync_YieldsTheTrailingLineWithoutANewline()
    {
        // A container that writes its readiness marker with 'printf' and no '\n' still has to be reported, otherwise
        // Wait.ForLogMessage waits out the whole StartupTimeout for a message that was actually printed.
        var entries = await ReadAllAsync(
            (LogStream.Stdout, "starting\n"),
            (LogStream.Stdout, "SERVER READY"));

        Assert.Equal(["starting", "SERVER READY"], entries.Select(entry => entry.Message));
    }

    [Fact]
    public async Task ReadMultiplexedLogsAsync_DoesNotEndTheTrailingLineWithACarriageReturn()
    {
        // A container writing CRLF endings that is killed between the CR and the LF. TryReadLine already drops the CR
        // from every complete line, so the trailing one must not be the only line that keeps it.
        var entries = await ReadAllAsync((LogStream.Stdout, "starting\r\nSERVER READY\r"));

        Assert.Equal(["starting", "SERVER READY"], entries.Select(entry => entry.Message));
    }

    [Fact]
    public async Task ReadMultiplexedLogsAsync_KeepsTheTimestampOfTheTrailingLine()
    {
        var entries = await ReadAllAsync((LogStream.Stdout, "2026-08-27T10:11:12.0000000Z SERVER READY"));

        var entry = Assert.Single(entries);
        Assert.Equal("SERVER READY", entry.Message);
        Assert.Equal(DateTimeOffset.Parse("2026-08-27T10:11:12.0000000Z", CultureInfo.InvariantCulture), entry.Timestamp);
    }

    private static async Task<List<LogEntry>> ReadAllAsync(params (LogStream Stream, string Text)[] frames)
    {
        using var stream = new MemoryStream(BuildFrames(frames));

        var entries = new List<LogEntry>();
        await foreach (var entry in DockerApiRuntime.ReadMultiplexedLogsAsync(stream, CancellationToken.None))
            entries.Add(entry);

        return entries;
    }

    /// <summary>Builds the stream the Docker API returns for a container without a TTY: each payload is prefixed by an 8-byte header whose first byte is the stream and whose last four bytes are the big-endian payload length.</summary>
    private static byte[] BuildFrames((LogStream Stream, string Text)[] frames)
    {
        var result = new List<byte>();
        foreach (var (logStream, text) in frames)
        {
            var payload = Encoding.UTF8.GetBytes(text);
            var header = new byte[8];
            header[0] = logStream is LogStream.Stderr ? (byte)2 : (byte)1;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), payload.Length);

            result.AddRange(header);
            result.AddRange(payload);
        }

        return [.. result];
    }
}
