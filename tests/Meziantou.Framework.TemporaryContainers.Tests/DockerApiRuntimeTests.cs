using System.Formats.Tar;
using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.TemporaryContainers.Internals;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class DockerApiRuntimeTests
{
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

    [Fact]
    public async Task ReadMultiplexedTextAsync_DecodesACharacterSplitAcrossFrames()
    {
        // The daemon cuts frames wherever its reads end, which can be in the middle of a character.
        var bytes = Encoding.UTF8.GetBytes("héllo 🎉 wörld");
        var frames = new List<(LogStream Stream, byte[] Payload)>();
        for (var i = 0; i < bytes.Length; i++)
            frames.Add((LogStream.Stdout, [bytes[i]]));

        frames.Add((LogStream.Stderr, Encoding.UTF8.GetBytes("é")[..1]));
        frames.Add((LogStream.Stderr, Encoding.UTF8.GetBytes("é")[1..]));

        using var stream = new OneByteAtATimeStream(BuildRawFrames(frames));
        var (standardOutput, standardError) = await DockerApiRuntime.ReadMultiplexedTextAsync(stream, XunitCancellationToken);

        Assert.Equal("héllo 🎉 wörld", standardOutput);
        Assert.Equal("é", standardError);
    }

    [Fact]
    public async Task ReadMultiplexedLogsAsync_ReadsAStreamThatReturnsOneByteAtATime()
    {
        using var stream = new OneByteAtATimeStream(BuildFrames([(LogStream.Stdout, "first\nsecond\n")]));

        var entries = new List<LogEntry>();
        await foreach (var entry in DockerApiRuntime.ReadMultiplexedLogsAsync(stream, XunitCancellationToken))
            entries.Add(entry);

        Assert.Equal(["first", "second"], entries.Select(entry => entry.Message));
    }

    [Fact]
    public async Task ReadMultiplexedLogsAsync_ReportsAStreamThatEndsInTheMiddleOfAFrame()
    {
        var bytes = BuildFrames([(LogStream.Stdout, "truncated\n")]);
        using var stream = new MemoryStream(bytes[..^3]);

        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await foreach (var entry in DockerApiRuntime.ReadMultiplexedLogsAsync(stream, XunitCancellationToken))
                _ = entry;
        });
    }

    [Fact]
    public async Task ReadSingleFileAsync_ReadsAnEmptyFile()
    {
        // The tar reader has no data stream at all for an entry of size 0.
        using var archive = await CreateArchiveAsync(writer => writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "empty.txt")));

        await using var content = await DockerApiTarArchive.ReadSingleFileAsync(archive, "/app/empty.txt", XunitCancellationToken);

        Assert.Equal(0, content.Length);
    }

    [Fact]
    public async Task ReadSingleFileAsync_RejectsADirectory()
    {
        // A directory comes with the files below it: the content of the first one is not the content of the path.
        using var archive = await CreateArchiveAsync(writer =>
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "etc/"));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "etc/hostname") { DataStream = new MemoryStream("host"u8.ToArray()) });
        });

        await Assert.ThrowsAsync<FileNotFoundException>(() => DockerApiTarArchive.ReadSingleFileAsync(archive, "/etc", XunitCancellationToken));
    }

    [Fact]
    public async Task ReadSingleFileAsync_RejectsALinkThatWasNotResolved()
    {
        using var archive = await CreateArchiveAsync(writer => writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "os-release") { LinkName = "../usr/lib/os-release" }));

        await Assert.ThrowsAsync<FileNotFoundException>(() => DockerApiTarArchive.ReadSingleFileAsync(archive, "/etc/os-release", XunitCancellationToken));
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_RecreatesEmptyFilesAndLinks()
    {
        using var destination = TemporaryDirectory.Create();
        using var archive = await CreateArchiveAsync(writer =>
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "data/"));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "data/empty.txt"));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "data/content.txt") { DataStream = new MemoryStream("content"u8.ToArray()) });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "data/hardlink.txt") { LinkName = "data/content.txt" });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "data/symlink.txt") { LinkName = "content.txt" });
        });

        await DockerApiTarArchive.ExtractToDirectoryAsync(archive, destination.FullPath, stripFirstSegment: true, XunitCancellationToken);

        Assert.Equal("", await File.ReadAllTextAsync(destination.FullPath / "empty.txt", XunitCancellationToken));
        Assert.Equal("content", await File.ReadAllTextAsync(destination.FullPath / "hardlink.txt", XunitCancellationToken));
        Assert.Equal("content.txt", new FileInfo(destination.FullPath / "symlink.txt").LinkTarget);
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_AcceptsADestinationWithATrailingSeparator()
    {
        using var destination = TemporaryDirectory.Create();
        using var archive = await CreateArchiveAsync(writer => writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "a.txt") { DataStream = new MemoryStream("a"u8.ToArray()) }));

        await DockerApiTarArchive.ExtractToDirectoryAsync(archive, destination.FullPath.Value + Path.DirectorySeparatorChar, stripFirstSegment: false, XunitCancellationToken);

        Assert.Equal("a", await File.ReadAllTextAsync(destination.FullPath / "a.txt", XunitCancellationToken));
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("data/../../escaped.txt")]
    public async Task ExtractToDirectoryAsync_RejectsAnEntryOutsideOfTheDestination(string entryName)
    {
        using var destination = TemporaryDirectory.Create();
        using var archive = await CreateArchiveAsync(writer => writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = new MemoryStream("x"u8.ToArray()) }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DockerApiTarArchive.ExtractToDirectoryAsync(archive, destination.FullPath / "out", stripFirstSegment: false, XunitCancellationToken));
        Assert.False(File.Exists(destination.FullPath / "escaped.txt"));
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_DoesNotWriteThroughALinkOfTheArchive()
    {
        // A link to a directory outside of the destination, then a file below the link: writing it would land outside.
        using var destination = TemporaryDirectory.Create();
        using var outside = TemporaryDirectory.Create();
        using var archive = await CreateArchiveAsync(writer =>
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = outside.FullPath });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "link/escaped.txt") { DataStream = new MemoryStream("x"u8.ToArray()) });
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => DockerApiTarArchive.ExtractToDirectoryAsync(archive, destination.FullPath, stripFirstSegment: false, XunitCancellationToken));
        Assert.False(File.Exists(outside.FullPath / "escaped.txt"));
    }

    [Fact]
    public async Task CreateForDirectoryAsync_KeepsTheExecutableBitAndTheLinks()
    {
        global::Xunit.Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows files have no executable bit.");

        using var context = TemporaryDirectory.Create();
        var script = context.FullPath / "entrypoint.sh";
        await File.WriteAllTextAsync(script, "#!/bin/sh\n", XunitCancellationToken);
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        await File.WriteAllTextAsync(context.FullPath / "empty.txt", "", XunitCancellationToken);
        Directory.CreateDirectory(context.FullPath / "sub");
        File.CreateSymbolicLink(context.FullPath / "link", "entrypoint.sh");

        await using var archive = await DockerApiTarArchive.CreateForDirectoryAsync(context.FullPath, entryPrefix: "", additionalFile: null, XunitCancellationToken);

        var entries = new Dictionary<string, TarEntry>(StringComparer.Ordinal);
        using var reader = new TarReader(archive);
        while (await reader.GetNextEntryAsync(copyData: false, XunitCancellationToken) is { } entry)
            entries[entry.Name] = entry;

        Assert.True(entries["entrypoint.sh"].Mode.HasFlag(UnixFileMode.UserExecute));
        Assert.Equal(TarEntryType.RegularFile, entries["empty.txt"].EntryType);
        Assert.Equal(TarEntryType.Directory, entries["sub/"].EntryType);
        Assert.Equal(TarEntryType.SymbolicLink, entries["link"].EntryType);
        Assert.Equal("entrypoint.sh", entries["link"].LinkName);
    }

    [Fact]
    public async Task CreateForDirectoryAsync_WritesAContextOnlyTheCurrentUserCanRead()
    {
        global::Xunit.Assert.SkipWhen(OperatingSystem.IsWindows(), "The temporary directory is private on Windows.");

        using var context = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(context.FullPath / ".env", "SECRET=1", XunitCancellationToken);

        await using var archive = await DockerApiTarArchive.CreateForDirectoryAsync(context.FullPath, entryPrefix: "", additionalFile: null, XunitCancellationToken);

        var path = Assert.IsType<TemporaryFileStream>(archive).FilePath;
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Reaper_FormatsDurationsTheWayGoParsesThem()
    {
        Assert.Equal("10s", ContainerReaper.FormatDuration(TimeSpan.FromSeconds(10)));
        Assert.Equal("60s", ContainerReaper.FormatDuration(TimeSpan.FromMinutes(1)));
        Assert.Equal("1500ms", ContainerReaper.FormatDuration(TimeSpan.FromMilliseconds(1500)));
    }

    [Fact]
    public void Reaper_FiltersOnTheSessionLabel()
    {
        Assert.Equal("label=meziantou.tc.session%3Dabc\n", ContainerReaper.BuildFilter("abc"));
    }

    private static async Task<MemoryStream> CreateArchiveAsync(Action<TarWriter> write)
    {
        var archive = new MemoryStream();
        await using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            write(writer);
        }

        archive.Position = 0;
        return archive;
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
        => BuildRawFrames([.. frames.Select(frame => (frame.Stream, Encoding.UTF8.GetBytes(frame.Text)))]);

    private static byte[] BuildRawFrames(List<(LogStream Stream, byte[] Payload)> frames)
    {
        var result = new List<byte>();
        foreach (var (logStream, payload) in frames)
        {
            var header = new byte[8];
            header[0] = logStream is LogStream.Stderr ? (byte)2 : (byte)1;
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), payload.Length);

            result.AddRange(header);
            result.AddRange(payload);
        }

        return [.. result];
    }

    /// <summary>A stream that returns one byte per read, as a socket under load can.</summary>
    private sealed class OneByteAtATimeStream(byte[] content) : MemoryStream(content)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }
}
