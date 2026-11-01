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

        await using var archive = await DockerApiTarArchive.CreateForDirectoryAsync(context.FullPath, entryPrefix: "", additionalFile: null, ignore: null, XunitCancellationToken);

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

        await using var archive = await DockerApiTarArchive.CreateForDirectoryAsync(context.FullPath, entryPrefix: "", additionalFile: null, ignore: null, XunitCancellationToken);

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

    [Theory]
    [InlineData("secret.txt", "secret.txt", true)]
    [InlineData("secret.txt", "sub/secret.txt", false)]
    [InlineData("*.log", "app.log", true)]
    [InlineData("*.log", "logs/app.log", false)]
    [InlineData("*/*.log", "logs/app.log", true)]
    [InlineData("**/*.log", "app.log", true)]
    [InlineData("**/*.log", "a/b/c/app.log", true)]
    [InlineData("bin", "bin/Debug/app.dll", true)]
    [InlineData("/bin/", "bin/Debug/app.dll", true)]
    [InlineData("./obj", "obj/project.assets.json", true)]
    [InlineData("src/**", "src/a/b.cs", true)]
    [InlineData("src/**", "srcfile", false)]
    [InlineData("**", "anything/at/all", true)]
    [InlineData("file?.txt", "file1.txt", true)]
    [InlineData("file?.txt", "file10.txt", false)]
    [InlineData("file[0-9].txt", "file5.txt", true)]
    [InlineData("file[^0-9].txt", "file5.txt", false)]
    [InlineData("file[^0-9].txt", "filea.txt", true)]
    [InlineData("a[/]b", "a/b", false)]
    [InlineData("data.json", "data_json", false)]
    [InlineData("  spaced.txt  ", "spaced.txt", true)]
    [InlineData("../outside", "outside", false)]
    public void DockerIgnore_MatchesLikeTheDockerCli(string pattern, string path, bool excluded)
    {
        var ignore = DockerIgnore.Parse(pattern, alwaysIncludedPaths: []);

        Assert.Equal(excluded, ignore.IsExcluded(path));
    }

    [Fact]
    public void DockerIgnore_ABackslashEscapesAWildcard()
    {
        global::Xunit.Assert.SkipWhen(OperatingSystem.IsWindows(), "A backslash is a path separator on Windows.");

        var ignore = DockerIgnore.Parse("file\\*.txt", alwaysIncludedPaths: []);

        Assert.True(ignore.IsExcluded("file*.txt"));
        Assert.False(ignore.IsExcluded("file1.txt"));
    }

    [Fact]
    public void DockerIgnore_TheLastMatchingPatternWins()
    {
        var ignore = DockerIgnore.Parse("""
            # Build outputs
            *.md
            !README.md
            docs
            !docs/keep.txt
            README-secret.md
            """, alwaysIncludedPaths: []);

        Assert.True(ignore.IsExcluded("CHANGELOG.md"));
        Assert.False(ignore.IsExcluded("README.md"));
        Assert.True(ignore.IsExcluded("README-secret.md"));
        Assert.True(ignore.IsExcluded("docs/guide.txt"));
        Assert.False(ignore.IsExcluded("docs/keep.txt"));
        Assert.True(ignore.HasExceptions);
    }

    [Fact]
    public void DockerIgnore_NeverExcludesTheDockerfileAndTheIgnoreFile()
    {
        var ignore = DockerIgnore.Parse("*\n", alwaysIncludedPaths: ["build/Dockerfile", ".dockerignore"]);

        Assert.True(ignore.IsExcluded("src/app.cs"));
        Assert.False(ignore.IsExcluded("build/Dockerfile"));
        Assert.False(ignore.IsExcluded(".dockerignore"));
        Assert.True(ignore.CanIncludeBelow("build"));
        Assert.False(ignore.CanIncludeBelow("node_modules"));
    }

    [Theory]
    [InlineData("!src/keep.txt", "src", true)]
    [InlineData("!src/keep.txt", "src/sub", false)]
    [InlineData("!src/keep.txt", "other", false)]
    [InlineData("!*.txt", "anything", true)]
    [InlineData("!src/*/keep.txt", "src/sub", true)]
    [InlineData("!**/keep.txt", "a/b", true)]
    public void DockerIgnore_WalksAnExcludedDirectoryOnlyWhenAnExceptionCanMatchBelowIt(string exception, string directory, bool expected)
    {
        var ignore = DockerIgnore.Parse("*\n" + exception, alwaysIncludedPaths: []);

        Assert.Equal(expected, ignore.CanIncludeBelow(directory));
    }

    [Fact]
    public async Task CreateForDirectoryAsync_AppliesTheIgnoreFile()
    {
        using var context = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(context.FullPath / "Dockerfile", "FROM scratch", XunitCancellationToken);
        await File.WriteAllTextAsync(context.FullPath / ".dockerignore", "*\n!src\nsrc/**/*.secret\n", XunitCancellationToken);
        await File.WriteAllTextAsync(context.FullPath / ".env", "SECRET=1", XunitCancellationToken);
        Directory.CreateDirectory(context.FullPath / "src" / "nested");
        await File.WriteAllTextAsync(context.FullPath / "src" / "app.cs", "", XunitCancellationToken);
        await File.WriteAllTextAsync(context.FullPath / "src" / "nested" / "key.secret", "", XunitCancellationToken);
        Directory.CreateDirectory(context.FullPath / "node_modules" / "package");
        await File.WriteAllTextAsync(context.FullPath / "node_modules" / "package" / "index.js", "", XunitCancellationToken);

        var ignore = await DockerIgnore.LoadAsync(context.FullPath, context.FullPath / "Dockerfile", "Dockerfile", XunitCancellationToken);
        await using var archive = await DockerApiTarArchive.CreateForDirectoryAsync(context.FullPath, entryPrefix: "", additionalFile: null, ignore, XunitCancellationToken);

        Assert.Equal([".dockerignore", "Dockerfile", "src/", "src/app.cs", "src/nested/"], await ReadEntryNamesAsync(archive));
    }

    [Fact]
    public async Task CreateForDirectoryAsync_KeepsAFileAnExceptionBringsBackFromAnExcludedDirectory()
    {
        using var context = TemporaryDirectory.Create();
        Directory.CreateDirectory(context.FullPath / "config");
        await File.WriteAllTextAsync(context.FullPath / "config" / "appsettings.json", "", XunitCancellationToken);
        await File.WriteAllTextAsync(context.FullPath / "config" / "secrets.json", "", XunitCancellationToken);
        await File.WriteAllTextAsync(context.FullPath / "Dockerfile", "FROM scratch", XunitCancellationToken);
        await File.WriteAllTextAsync(context.FullPath / "Dockerfile.dockerignore", "config\n!config/appsettings.json\n", XunitCancellationToken);
        await File.WriteAllTextAsync(context.FullPath / ".dockerignore", "Dockerfile.dockerignore\n", XunitCancellationToken);

        // The ignore file named after the Dockerfile takes precedence over the one of the context.
        var ignore = await DockerIgnore.LoadAsync(context.FullPath, context.FullPath / "Dockerfile", "Dockerfile", XunitCancellationToken);
        await using var archive = await DockerApiTarArchive.CreateForDirectoryAsync(context.FullPath, entryPrefix: "", additionalFile: null, ignore, XunitCancellationToken);

        Assert.Equal([".dockerignore", "Dockerfile", "Dockerfile.dockerignore", "config/appsettings.json"], await ReadEntryNamesAsync(archive));
    }

    [Fact]
    public async Task DockerIgnore_LoadAsync_ReturnsNullWithoutAnIgnoreFile()
    {
        using var context = TemporaryDirectory.Create();

        Assert.Null(await DockerIgnore.LoadAsync(context.FullPath, context.FullPath / "Dockerfile", "Dockerfile", XunitCancellationToken));
    }

    [Fact]
    public void DockerfileBaseImages_ListsTheImagesTheBuildPulls()
    {
        var images = DockerfileBaseImages.Parse("""
            # syntax=docker/dockerfile:1
            ARG REGISTRY=registry.example.com
            ARG VERSION
            FROM --platform=linux/amd64 ${REGISTRY}/sdk:10.0 AS build
            RUN dotnet publish
            FROM build AS test
            FROM $REGISTRY/runtime:$VERSION
            FROM scratch
            FROM ghcr.io/owner/tool:1 \
                AS tool
            COPY --from=build /app /app
            COPY --from=docker.io/library/busybox:1.38 /bin/sh /bin/sh
            COPY --from=0 /app /app
            FROM mcr.microsoft.com/dotnet/runtime:10.0
            """, [new("VERSION", "10.0")]);

        Assert.Equal(["registry.example.com/sdk:10.0", "registry.example.com/runtime:10.0", "ghcr.io/owner/tool:1", "docker.io/library/busybox:1.38", "mcr.microsoft.com/dotnet/runtime:10.0"], images);
    }

    [Fact]
    public void DockerfileBaseImages_SkipsAnImageWhoseArgumentHasNoValue()
    {
        var images = DockerfileBaseImages.Parse("ARG BASE\nFROM ${BASE}\nFROM alpine:3\n", []);

        Assert.Equal(["alpine:3"], images);
    }

    [Theory]
    [InlineData("linux", """{"os":"linux"}""")]
    [InlineData("linux/amd64", """{"os":"linux","architecture":"amd64"}""")]
    [InlineData("linux/arm64/v8", """{"os":"linux","architecture":"arm64","variant":"v8"}""")]
    public void FormatPlatformAsJson_WritesTheObjectTheImageEndpointsExpect(string platform, string expected)
    {
        Assert.Equal(expected, DockerApiRuntime.FormatPlatformAsJson(platform));
    }

    private static async Task<List<string>> ReadEntryNamesAsync(Stream archive)
    {
        var names = new List<string>();
        using var reader = new TarReader(archive);
        while (await reader.GetNextEntryAsync(copyData: false, XunitCancellationToken) is { } entry)
            names.Add(entry.Name);

        names.Sort(StringComparer.Ordinal);
        return names;
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
