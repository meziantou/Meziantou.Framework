using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.WasmEngine.Wasi.Internal;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WasmEngine.Wasi.Tests;

public sealed class WasiImplementationTests
{
    private const ushort Create = WasiRights.OpenCreate;
    private const ushort Directory = WasiRights.OpenDirectory;
    private const ushort Exclusive = WasiRights.OpenExclusive;
    private const ushort Truncate = WasiRights.OpenTruncate;

    public static TheoryData<string> FileSystems => ["memory", "host"];

    /// <summary>A file system for a test, with the directory of a host file system inside a parent that holds a secret file.</summary>
    private sealed class TestFileSystem : IDisposable
    {
        private readonly TemporaryDirectory? _directory;

        public TestFileSystem(string kind, bool isReadOnly = false)
        {
            if (kind == "memory")
            {
                FileSystem = new WasiInMemoryFileSystem(isReadOnly);
                return;
            }

            _directory = TemporaryDirectory.Create();
            _directory.CreateTextFile("secret.txt", "secret");
            var root = _directory.CreateDirectory("root");
            FileSystem = new WasiHostFileSystem(root, isReadOnly);
        }

        public WasiFileSystem FileSystem { get; }

        public bool IsHost => _directory is not null;

        public bool SupportsSymbolicLinks => !IsHost || CanCreateSymbolicLinks.Value;

        public void Dispose() => _directory?.Dispose();
    }

    private static readonly Lazy<bool> CanCreateSymbolicLinks = new(() =>
    {
        using var directory = TemporaryDirectory.Create();
        try
        {
            File.CreateSymbolicLink(directory.GetFullPath("link"), "target");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    });

    [Fact]
    public void Arguments_AreNullTerminatedStrings()
    {
        using var harness = new WasiHarness(new WasiOptions { Arguments = ["program", "é"], EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal) { ["KEY"] = "value" } });
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.ArgsSizesGet(harness.Memory, 0, 4)));
        Assert.Equal(2u, harness.ReadUInt32(0));
        Assert.Equal(8u + 3u, harness.ReadUInt32(4));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.ArgsGet(harness.Memory, 16, 100)));
        Assert.Equal(100u, harness.ReadUInt32(16));
        Assert.Equal(108u, harness.ReadUInt32(20));
        Assert.Equal("program\0é\0", Encoding.UTF8.GetString(harness.Memory, 100, 11));

        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.EnvironSizesGet(harness.Memory, 0, 4)));
        Assert.Equal(1u, harness.ReadUInt32(0));
        Assert.Equal(10u, harness.ReadUInt32(4));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.EnvironGet(harness.Memory, 16, 200)));
        Assert.Equal("KEY=value\0", Encoding.UTF8.GetString(harness.Memory, 200, 10));

        // The strings must fit in the memory
        Assert.Equal(WasiErrno.Fault, WasiHarness.Call(() => harness.Wasi.ArgsGet(harness.Memory, 16, 65530)));
        Assert.Equal(WasiErrno.Fault, WasiHarness.Call(() => harness.Wasi.ArgsGet(harness.Memory, uint.MaxValue - 3, 100)));
        Assert.Equal(WasiErrno.Fault, WasiHarness.Call(() => harness.Wasi.ArgsSizesGet(harness.Memory, 65535, 0)));
    }

    [Fact]
    public void Clocks_UseTheTimeProvider()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        using var harness = new WasiHarness(new WasiOptions { TimeProvider = time });
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.ClockTimeGet(harness.Memory, 0, 0, 0)));
        Assert.Equal((ulong)(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero) - DateTimeOffset.UnixEpoch).Ticks * 100, harness.ReadUInt64(0));

        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.ClockTimeGet(harness.Memory, 1, 0, 0)));
        var monotonic = harness.ReadUInt64(0);
        time.Advance(TimeSpan.FromMilliseconds(5));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.ClockTimeGet(harness.Memory, 1, 0, 0)));
        Assert.Equal(5_000_000UL, harness.ReadUInt64(0) - monotonic);

        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.ClockTimeGet(harness.Memory, 2, 0, 0)));
        Assert.Equal(5_000_000UL, harness.ReadUInt64(0));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.ClockResGet(harness.Memory, 1, 0)));
        Assert.True(harness.ReadUInt64(0) > 0);

        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.ClockTimeGet(harness.Memory, 4, 0, 0)));
        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.ClockResGet(harness.Memory, 4, 0)));
        Assert.Equal(WasiErrno.Fault, WasiHarness.Call(() => harness.Wasi.ClockTimeGet(harness.Memory, 0, 0, 65532)));
    }

    [Fact]
    public void RandomGet_FillsTheBuffer()
    {
        using var harness = new WasiHarness(new WasiOptions { RandomSource = buffer => buffer.Fill(42) });
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.RandomGet(harness.Memory, 10, 5)));
        Assert.Equal([0, 42, 42, 42, 42, 42, 0], harness.Memory[9..16]);
        Assert.Equal(WasiErrno.Fault, WasiHarness.Call(() => harness.Wasi.RandomGet(harness.Memory, 65535, 2)));

        using var system = new WasiHarness(new WasiOptions());
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => system.Wasi.RandomGet(system.Memory, 0, 0)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => system.Wasi.RandomGet(system.Memory, 0, 64)));
    }

    [Fact]
    public void StandardStreams_ReadAndWriteTheStreams()
    {
        using var input = new MemoryStream("input"u8.ToArray());
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        using var harness = new WasiHarness(new WasiOptions { StandardInput = input, StandardOutput = output, StandardError = error });
        Assert.Equal(6u, harness.Write(1, "output"));
        Assert.Equal(5u, harness.Write(2, "error"));
        Assert.Equal("input", harness.Read(0));
        Assert.Equal("", harness.Read(0));
        Assert.Equal("output", Encoding.UTF8.GetString(output.ToArray()));
        Assert.Equal("error", Encoding.UTF8.GetString(error.ToArray()));

        Assert.Equal(WasiErrno.BadFileDescriptor, harness.TryWrite(0, "x", out _));
        Assert.Equal(WasiErrno.BadFileDescriptor, harness.TryRead(1, 1, out _));
        Assert.Equal(WasiErrno.BadFileDescriptor, harness.TryWrite(3, "x", out _));
        Assert.Equal(WasiErrno.NotCapable, WasiHarness.Call(() => harness.Wasi.FdTell(harness.Memory, 1, 0)));

        // A terminal has the type of a character device, and cannot seek
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFdstatGet(harness.Memory, 1, 0)));
        Assert.Equal((byte)WasiFileType.CharacterDevice, harness.Memory[0]);
        Assert.Equal(0UL, harness.ReadUInt64(8) & (WasiRights.FdSeek | WasiRights.FdTell));

        // The output of a stream that is not given is discarded
        using var discarded = new WasiHarness(new WasiOptions());
        Assert.Equal(3u, discarded.Write(1, "abc"));
        Assert.Equal("", discarded.Read(0));
    }

    [Fact]
    public void Prestat_DescribesThePreopenedDirectories()
    {
        using var harness = new WasiHarness(new WasiOptions
        {
            PreopenedDirectories = [new WasiPreopenedDirectory("/", new WasiInMemoryFileSystem()), new WasiPreopenedDirectory("/data", new WasiInMemoryFileSystem())],
        });

        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdPrestatGet(harness.Memory, 4, 0)));
        Assert.Equal(0, harness.Memory[0]);
        Assert.Equal(5u, harness.ReadUInt32(4));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdPrestatDirName(harness.Memory, 4, 100, 5)));
        Assert.Equal("/data", Encoding.UTF8.GetString(harness.Memory, 100, 5));
        Assert.Equal(WasiErrno.NameTooLong, WasiHarness.Call(() => harness.Wasi.FdPrestatDirName(harness.Memory, 4, 100, 4)));
        Assert.Equal(WasiErrno.BadFileDescriptor, WasiHarness.Call(() => harness.Wasi.FdPrestatGet(harness.Memory, 5, 0)));
        Assert.Equal(WasiErrno.BadFileDescriptor, WasiHarness.Call(() => harness.Wasi.FdPrestatGet(harness.Memory, 1, 0)));
        Assert.Equal(WasiErrno.BadFileDescriptor, WasiHarness.Call(() => harness.Wasi.FdPrestatGet(harness.Memory, -1, 0)));

        // A directory opened from a preopened directory is not preopened
        var fd = harness.Open(".", Directory);
        Assert.Equal(WasiErrno.BadFileDescriptor, WasiHarness.Call(() => harness.Wasi.FdPrestatGet(harness.Memory, fd, 0)));
    }

    [Theory]
    [MemberData(nameof(FileSystems))]
    public void Files_ReadWriteSeekAndStat(string kind)
    {
        using var files = new TestFileSystem(kind);
        using var harness = new WasiHarness(files.FileSystem);
        var fd = harness.Create("file.txt", "Hello, world");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdSeek(harness.Memory, fd, 0, 0, 0)));
        Assert.Equal(0UL, harness.ReadUInt64(0));
        Assert.Equal("Hello", harness.Read(fd, 5));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdTell(harness.Memory, fd, 0)));
        Assert.Equal(5UL, harness.ReadUInt64(0));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdSeek(harness.Memory, fd, -5, 2, 0)));
        Assert.Equal(7UL, harness.ReadUInt64(0));
        Assert.Equal("world", harness.Read(fd));
        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.FdSeek(harness.Memory, fd, -100, 1, 0)));
        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.FdSeek(harness.Memory, fd, 0, 3, 0)));

        // pwrite and pread do not move the position
        var (text, length) = harness.String("HELLO");
        BinaryPrimitives.WriteUInt32LittleEndian(harness.Memory.AsSpan(16), text);
        BinaryPrimitives.WriteUInt32LittleEndian(harness.Memory.AsSpan(20), length);
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdPwrite(harness.Memory, fd, 16, 1, 0, 24)));
        Assert.Equal(5u, harness.ReadUInt32(24));
        BinaryPrimitives.WriteUInt32LittleEndian(harness.Memory.AsSpan(16), 512);
        BinaryPrimitives.WriteUInt32LittleEndian(harness.Memory.AsSpan(20), 7);
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdPread(harness.Memory, fd, 16, 1, 0, 24)));
        Assert.Equal("HELLO, ", Encoding.UTF8.GetString(harness.Memory, 512, (int)harness.ReadUInt32(24)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdTell(harness.Memory, fd, 0)));
        Assert.Equal(12UL, harness.ReadUInt64(0));

        // The status of the file
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFilestatGet(harness.Memory, fd, 64)));
        Assert.Equal((byte)WasiFileType.RegularFile, harness.Memory[64 + 16]);
        Assert.Equal(12UL, harness.ReadUInt64(64 + 32));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFilestatSetSize(fd, 4)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFilestatGet(harness.Memory, fd, 64)));
        Assert.Equal(4UL, harness.ReadUInt64(64 + 32));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdAllocate(fd, 0, 10)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFilestatGet(harness.Memory, fd, 64)));
        Assert.Equal(10UL, harness.ReadUInt64(64 + 32));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdSync(fd)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdDatasync(fd)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdAdvise(fd, 0, 0, 1)));
        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.FdAdvise(fd, 0, 0, 6)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(fd)));
        Assert.Equal(WasiErrno.BadFileDescriptor, WasiHarness.Call(() => harness.Wasi.FdClose(fd)));
        Assert.Equal(WasiErrno.BadFileDescriptor, harness.TryRead(fd, 1, out _));
    }

    [Theory]
    [MemberData(nameof(FileSystems))]
    public void Files_OpenFlags(string kind)
    {
        using var files = new TestFileSystem(kind);
        using var harness = new WasiHarness(files.FileSystem);
        Assert.Equal(WasiErrno.NoEntry, harness.TryOpen("missing.txt", out _));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(harness.Create("file.txt", "content"))));
        Assert.Equal(WasiErrno.Exists, harness.TryOpen("file.txt", out _, Create | Exclusive));
        Assert.Equal(WasiErrno.NotDirectory, harness.TryOpen("file.txt", out _, Directory));
        Assert.Equal(WasiErrno.NotDirectory, harness.TryOpen("file.txt/", out _));
        Assert.Equal(WasiErrno.IsDirectory, harness.TryOpen("new/", out _, Create));
        Assert.Equal(WasiErrno.InvalidArgument, harness.TryOpen("file.txt", out _, 16));

        // Truncate, and append
        var fd = harness.Open("file.txt", Truncate);
        Assert.Equal("", harness.Read(fd));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(fd)));
        fd = harness.Open("file.txt", fdFlags: WasiRights.FdFlagAppend);
        harness.Write(fd, "a");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdSeek(harness.Memory, fd, 0, 0, 0)));
        harness.Write(fd, "b");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFdstatGet(harness.Memory, fd, 0)));
        Assert.Equal(WasiRights.FdFlagAppend, BinaryPrimitives.ReadUInt16LittleEndian(harness.Memory.AsSpan(2)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFdstatSetFlags(fd, 0)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdSeek(harness.Memory, fd, 0, 0, 0)));
        harness.Write(fd, "c");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdSeek(harness.Memory, fd, 0, 0, 0)));
        Assert.Equal("cb", harness.Read(fd));
        Assert.Equal(WasiErrno.NotSupported, WasiHarness.Call(() => harness.Wasi.FdFdstatSetFlags(fd, WasiRights.FdFlagSync)));

        // A directory cannot be opened to write it
        Assert.Equal(WasiErrno.IsDirectory, harness.TryOpen(".", out _, rights: WasiRights.FdWrite));
        Assert.Equal(WasiErrno.IsDirectory, harness.TryOpen(".", out _, Truncate));
        Assert.Equal(WasiErrno.Exists, harness.TryOpen(".", out _, Create | Exclusive));

        // The file is created with no rights: it is open for reading, but the module cannot read it, nor write it
        fd = harness.Open("empty.txt", Create, rights: 0);
        Assert.Equal(WasiErrno.NotCapable, harness.TryRead(fd, 1, out _));
        Assert.Equal(WasiErrno.BadFileDescriptor, harness.TryWrite(fd, "x", out _));
    }

    [Theory]
    [MemberData(nameof(FileSystems))]
    public void Directories_CreateListRenameAndRemove(string kind)
    {
        using var files = new TestFileSystem(kind);
        using var harness = new WasiHarness(files.FileSystem);
        Assert.Equal(WasiErrno.Success, harness.PathCall("dir", (address, length) => harness.Wasi.PathCreateDirectory(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.Exists, harness.PathCall("dir", (address, length) => harness.Wasi.PathCreateDirectory(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.NoEntry, harness.PathCall("missing/dir", (address, length) => harness.Wasi.PathCreateDirectory(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(harness.Create("dir/b.txt", "b"))));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(harness.Create("dir/a.txt", "a"))));

        // The entries are sorted, after "." and "..", whatever the size of the buffer
        var dir = harness.Open("dir", Directory);
        Assert.Equal([".", "..", "a.txt", "b.txt"], harness.ReadDirectory(dir));
        Assert.Equal([".", "..", "a.txt", "b.txt"], harness.ReadDirectory(dir, bufferSize: 30));
        var file = harness.Open("dir/a.txt");
        Assert.Equal(WasiErrno.NotDirectory, WasiHarness.Call(() => harness.Wasi.FdReaddir(harness.Memory, file, 0, 100, 0, 200)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdReaddir(harness.Memory, dir, 0, 100, 1000, 200)));
        Assert.Equal(0u, harness.ReadUInt32(200));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(file)));

        // Paths relative to the opened directory
        file = harness.Open("a.txt", directory: dir);
        Assert.Equal("a", harness.Read(file));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(file)));
        Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("../dir/a.txt", out _, directory: dir));
        Assert.Equal(WasiErrno.Success, harness.TryOpen("dir/../dir/a.txt", out file));

        // Windows cannot rename a directory whose files are open
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(file)));

        Assert.Equal(WasiErrno.NotEmpty, harness.PathCall("dir", (address, length) => harness.Wasi.PathRemoveDirectory(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.IsDirectory, harness.PathCall("dir", (address, length) => harness.Wasi.PathUnlinkFile(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.NotDirectory, harness.PathCall("dir/a.txt", (address, length) => harness.Wasi.PathRemoveDirectory(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.NotDirectory, harness.PathCall("dir/a.txt/", (address, length) => harness.Wasi.PathUnlinkFile(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.Busy, harness.PathCall(".", (address, length) => harness.Wasi.PathRemoveDirectory(harness.Memory, WasiHarness.Root, address, length)));

        // Rename a file over another file, a directory into another place, and a directory into itself
        Assert.Equal(WasiErrno.Success, Rename(harness, "dir/a.txt", "dir/b.txt"));
        file = harness.Open("dir/b.txt");
        Assert.Equal("a", harness.Read(file));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(file)));
        Assert.Equal(WasiErrno.NoEntry, harness.TryOpen("dir/a.txt", out _));
        Assert.Equal(WasiErrno.Success, Rename(harness, "dir", "renamed"));
        Assert.Equal(WasiErrno.InvalidArgument, Rename(harness, "renamed", "renamed/inside"));
        Assert.Equal(WasiErrno.IsDirectory, Rename(harness, "renamed/b.txt", "renamed"));
        Assert.Equal(WasiErrno.InvalidArgument, Rename(harness, "renamed", "renamed/b.txt"));
        Assert.Equal(WasiErrno.NoEntry, Rename(harness, "missing", "other"));
        Assert.Equal(WasiErrno.Success, harness.PathCall("empty", (address, length) => harness.Wasi.PathCreateDirectory(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.NotEmpty, Rename(harness, "empty", "renamed"));
        Assert.Equal(WasiErrno.Success, Rename(harness, "renamed", "empty"));
        Assert.Equal(WasiFileType.Directory, harness.GetFileType("empty"));

        Assert.Equal(WasiErrno.Success, harness.PathCall("empty/b.txt", (address, length) => harness.Wasi.PathUnlinkFile(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.Success, harness.PathCall("empty", (address, length) => harness.Wasi.PathRemoveDirectory(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.NoEntry, harness.PathCall("empty", (address, length) => harness.Wasi.PathRemoveDirectory(harness.Memory, WasiHarness.Root, address, length)));
    }

    private static WasiErrno Rename(WasiHarness harness, string source, string destination)
    {
        var (sourceAddress, sourceLength) = harness.String(source);
        var (destinationAddress, destinationLength) = harness.String(destination);
        return WasiHarness.Call(() => harness.Wasi.PathRename(harness.Memory, WasiHarness.Root, sourceAddress, sourceLength, WasiHarness.Root, destinationAddress, destinationLength));
    }

    private static WasiErrno Symlink(WasiHarness harness, string target, string path)
    {
        var (targetAddress, targetLength) = harness.String(target);
        var (pathAddress, pathLength) = harness.String(path);
        return WasiHarness.Call(() => harness.Wasi.PathSymlink(harness.Memory, targetAddress, targetLength, WasiHarness.Root, pathAddress, pathLength));
    }

    [Theory]
    [MemberData(nameof(FileSystems))]
    public void SymbolicLinks_AreFollowedInsideTheDirectory(string kind)
    {
        using var files = new TestFileSystem(kind);
        if (!files.SupportsSymbolicLinks)
        {
            global::Xunit.Assert.Skip("This machine does not allow creating symbolic links");
        }

        using var harness = new WasiHarness(files.FileSystem);
        harness.CreateDirectory("dir");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(harness.Create("dir/file.txt", "content"))));
        Assert.Equal(WasiErrno.Success, Symlink(harness, "dir/file.txt", "link"));
        Assert.Equal(WasiErrno.Success, Symlink(harness, "dir", "dirlink"));
        Assert.Equal(WasiErrno.Exists, Symlink(harness, "dir", "link"));
        Assert.Equal(WasiErrno.NotPermitted, Symlink(harness, "/etc/passwd", "absolute"));

        Assert.Equal("content", harness.Read(harness.Open("link")));
        Assert.Equal("content", harness.Read(harness.Open("dirlink/file.txt", lookupFlags: 0)));
        Assert.Equal(WasiFileType.SymbolicLink, harness.GetFileType("link"));
        Assert.Equal(WasiFileType.RegularFile, harness.GetFileType("link", follow: true));
        Assert.Equal(WasiFileType.Directory, harness.GetFileType("dirlink/", follow: false));

        // The last link is not followed without LOOKUP_SYMLINK_FOLLOW
        Assert.Equal(WasiErrno.Loop, harness.TryOpen("link", out _, lookupFlags: 0));

        // The target of a link
        var (path, length) = harness.String("link");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.PathReadlink(harness.Memory, WasiHarness.Root, path, length, 100, 50, 200)));
        Assert.Equal("dir/file.txt", Encoding.UTF8.GetString(harness.Memory, 100, (int)harness.ReadUInt32(200)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.PathReadlink(harness.Memory, WasiHarness.Root, path, length, 100, 3, 200)));
        Assert.Equal(3u, harness.ReadUInt32(200));
        (path, length) = harness.String("dir");
        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.PathReadlink(harness.Memory, WasiHarness.Root, path, length, 100, 50, 200)));

        // Unlinking a link does not delete its target
        Assert.Equal(WasiErrno.Success, harness.PathCall("dirlink", (address, pathLength) => harness.Wasi.PathUnlinkFile(harness.Memory, WasiHarness.Root, address, pathLength)));
        Assert.Equal(WasiFileType.Directory, harness.GetFileType("dir"));

        // A loop, and a dangling link
        Assert.Equal(WasiErrno.Success, Symlink(harness, "loop2", "loop1"));
        Assert.Equal(WasiErrno.Success, Symlink(harness, "loop1", "loop2"));
        Assert.Equal(WasiErrno.Loop, harness.TryOpen("loop1", out _));
        Assert.Equal(WasiErrno.Success, Symlink(harness, "missing", "dangling"));
        Assert.Equal(WasiErrno.NoEntry, harness.TryOpen("dangling", out _));
        Assert.Equal(WasiErrno.Success, harness.TryOpen("dangling", out _, Create));
        Assert.Equal(WasiFileType.RegularFile, harness.GetFileType("missing"));
    }

    [Theory]
    [MemberData(nameof(FileSystems))]
    public void Paths_CannotLeaveTheDirectory(string kind)
    {
        using var files = new TestFileSystem(kind);
        using var harness = new WasiHarness(files.FileSystem);
        harness.CreateDirectory("dir");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdClose(harness.Create("dir/file.txt", "content"))));

        Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("../secret.txt", out _));
        Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("dir/../../secret.txt", out _));
        Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("/secret.txt", out _));
        Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("..", out _, Directory));
        Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("../root/dir/file.txt", out _));
        Assert.Equal(WasiErrno.Success, harness.TryOpen("dir/../dir/./file.txt", out _));
        Assert.Equal(WasiErrno.NoEntry, harness.TryOpen("", out _));
        Assert.Equal(WasiErrno.IllegalByteSequence, harness.PathCall("x", (address, length) =>
        {
            harness.Memory[address] = 0xFF;
            return harness.Wasi.PathCreateDirectory(harness.Memory, WasiHarness.Root, address, length);
        }));
        Assert.Equal(WasiErrno.InvalidArgument, harness.TryOpen("dir\0/file.txt", out _));
        Assert.Equal(WasiErrno.NameTooLong, harness.PathCall(new string('a', 5000), (address, length) => harness.Wasi.PathCreateDirectory(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.NotCapable, Rename(harness, "dir/file.txt", "../moved.txt"));

        // The links cannot leave the directory either
        if (files.SupportsSymbolicLinks)
        {
            Assert.Equal(WasiErrno.Success, Symlink(harness, "../secret.txt", "escape"));
            Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("escape", out _));
            Assert.Equal(WasiErrno.Success, Symlink(harness, "../../secret.txt", "dir/escape"));
            Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("dir/escape", out _));
            Assert.Equal(WasiErrno.Success, Symlink(harness, "..", "dir/parent"));
            Assert.Equal(WasiErrno.Success, harness.TryOpen("dir/parent/dir/file.txt", out _));
            Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("dir/parent/../secret.txt", out _));
        }

        if (files.FileSystem is WasiInMemoryFileSystem memory)
        {
            memory.AddSymbolicLink("absolute", "/dir/file.txt");
            Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("absolute", out _));
        }
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("COM1")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    [InlineData("a*")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void HostFileSystem_RejectsTheNamesThatAreNotPortableOnWindows(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            global::Xunit.Assert.Skip("The names are checked on Windows");
        }

        using var files = new TestFileSystem("host");
        using var harness = new WasiHarness(files.FileSystem);
        Assert.Equal(WasiErrno.InvalidArgument, harness.TryOpen(name, out _, Create));
        Assert.Equal(WasiErrno.InvalidArgument, harness.PathCall(name, (address, length) => harness.Wasi.PathCreateDirectory(harness.Memory, WasiHarness.Root, address, length)));
    }

    [Fact]
    public void HostFileSystem_CanBeTheRootOfTheVolume()
    {
        using var directory = TemporaryDirectory.Create();
        string file = directory.CreateTextFile("file.txt", "content");
        var root = Path.GetPathRoot(file)!;
        var fileSystem = new WasiHostFileSystem(root, isReadOnly: true);
        var path = Path.GetRelativePath(root, file).Replace('\\', '/');
        Assert.Equal(WasiFileType.RegularFile, fileSystem.GetStat(path)?.Type);
    }

    [Fact]
    public void HostFileSystem_RejectsTheAbsoluteLinksOfWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            global::Xunit.Assert.Skip("The drives and the shares are paths of Windows");
        }

        using var directory = TemporaryDirectory.Create();
        var fileSystem = new WasiHostFileSystem(directory.CreateDirectory("root"));
        Assert.Equal(WasiErrno.NotPermitted, Assert.Throws<WasiException>(() => fileSystem.CreateSymbolicLink("drive", "C:/Windows")).Errno);
        Assert.Equal(WasiErrno.NotPermitted, Assert.Throws<WasiException>(() => fileSystem.CreateSymbolicLink("share", "\\\\server\\share")).Errno);
        Assert.Equal(WasiErrno.NotPermitted, Assert.Throws<WasiException>(() => fileSystem.CreateSymbolicLink("relative-drive", "C:file")).Errno);
    }

    [Theory]
    [MemberData(nameof(FileSystems))]
    public void Rename_ReplacesTheDestination(string kind)
    {
        using var files = new TestFileSystem(kind);
        using (var source = files.FileSystem.OpenFile("source.txt", FileMode.CreateNew, FileAccess.Write))
        {
            source.Write("new"u8);
        }

        using (var destination = files.FileSystem.OpenFile("destination.txt", FileMode.CreateNew, FileAccess.Write))
        {
            destination.Write("old"u8);
        }

        files.FileSystem.Replace("source.txt", "destination.txt");
        Assert.Null(files.FileSystem.GetStat("source.txt"));
        using var replaced = files.FileSystem.OpenFile("destination.txt", FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(replaced);
        Assert.Equal("new", reader.ReadToEnd());
    }

    [Theory]
    [MemberData(nameof(FileSystems))]
    public void ReadOnlyFileSystem_CannotBeChanged(string kind)
    {
        using var files = new TestFileSystem(kind, isReadOnly: true);
        switch (files.FileSystem)
        {
            case WasiInMemoryFileSystem memory:
                memory.AddFile("file.txt", "content"u8);
                break;

            case WasiHostFileSystem host:
                File.WriteAllText(Path.Combine(host.RootDirectory, "file.txt"), "content");
                break;
        }

        using var harness = new WasiHarness(files.FileSystem);
        Assert.Equal("content", harness.Read(harness.Open("file.txt", rights: WasiRights.FdRead)));
        Assert.Equal(WasiErrno.ReadOnlyFileSystem, harness.TryOpen("file.txt", out _, rights: WasiRights.FdWrite));
        Assert.Equal(WasiErrno.ReadOnlyFileSystem, harness.TryOpen("new.txt", out _, Create));
        Assert.Equal(WasiErrno.ReadOnlyFileSystem, harness.TryOpen("file.txt", out _, Truncate));
        Assert.Equal(WasiErrno.ReadOnlyFileSystem, harness.PathCall("dir", (address, length) => harness.Wasi.PathCreateDirectory(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.ReadOnlyFileSystem, harness.PathCall("file.txt", (address, length) => harness.Wasi.PathUnlinkFile(harness.Memory, WasiHarness.Root, address, length)));
        Assert.Equal(WasiErrno.ReadOnlyFileSystem, Rename(harness, "file.txt", "other.txt"));

        // A file opened only to read it has no right to write
        var fd = harness.Open("file.txt", rights: WasiRights.FdRead | WasiRights.FdSeek);
        Assert.Equal(WasiErrno.BadFileDescriptor, harness.TryWrite(fd, "x", out _));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFdstatGet(harness.Memory, fd, 0)));
        Assert.Equal(0UL, harness.ReadUInt64(8) & WasiRights.FdWrite);
    }

    [Fact]
    public void Rights_CanOnlyBeRemoved()
    {
        var files = new WasiInMemoryFileSystem();
        files.AddFile("file.txt", "content"u8);
        using var harness = new WasiHarness(files);
        var fd = harness.Open("file.txt");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFdstatSetRights(fd, WasiRights.FdWrite, 0)));
        Assert.Equal(WasiErrno.NotCapable, harness.TryRead(fd, 1, out _));
        Assert.Equal(WasiErrno.NotCapable, WasiHarness.Call(() => harness.Wasi.FdFdstatSetRights(fd, WasiRights.FdWrite | WasiRights.FdRead, 0)));

        // The directory gives its inheriting rights to the files opened from it
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFdstatSetRights(WasiHarness.Root, WasiRights.Directory, WasiRights.FdRead)));
        fd = harness.Open("file.txt");
        Assert.Equal("content", harness.Read(fd));
        Assert.Equal(WasiErrno.BadFileDescriptor, harness.TryWrite(fd, "x", out _));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFdstatGet(harness.Memory, fd, 0)));
        Assert.Equal(WasiRights.FdRead, harness.ReadUInt64(8));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFdstatSetRights(WasiHarness.Root, WasiRights.Directory & ~WasiRights.PathOpen, 0)));
        Assert.Equal(WasiErrno.NotCapable, harness.TryOpen("file.txt", out _));
    }

    [Fact]
    public void FileDescriptors_RenumberAndLimit()
    {
        var files = new WasiInMemoryFileSystem();
        files.AddFile("a.txt", "a"u8);
        files.AddFile("b.txt", "b"u8);
        using var harness = new WasiHarness(files);
        var a = harness.Open("a.txt");
        var b = harness.Open("b.txt");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdRenumber(a, b)));
        Assert.Equal("a", harness.Read(b));
        Assert.Equal(WasiErrno.BadFileDescriptor, harness.TryRead(a, 1, out _));
        Assert.Equal(WasiErrno.BadFileDescriptor, WasiHarness.Call(() => harness.Wasi.FdRenumber(a, b)));
        Assert.Equal(WasiErrno.BadFileDescriptor, WasiHarness.Call(() => harness.Wasi.FdRenumber(b, 100)));

        // The lowest free descriptor is reused
        Assert.Equal(a, harness.Open("a.txt"));

        while (harness.TryOpen(".", out _, Directory) == WasiErrno.Success)
        {
        }

        Assert.Equal(WasiImplementation.MaxFileDescriptors, harness.Wasi.FileDescriptorCount);
        Assert.Equal(WasiErrno.TooManyOpenFiles, harness.TryOpen(".", out _, Directory));
    }

    [Fact]
    public void Times_AreSetAndValidated()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero));
        var files = new WasiInMemoryFileSystem();
        files.AddFile("file.txt", "content"u8);
        using var harness = new WasiHarness(new WasiOptions { TimeProvider = time, PreopenedDirectories = [new WasiPreopenedDirectory("/", files)] });
        var fd = harness.Open("file.txt");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFilestatSetTimes(fd, 1_000_000_000, 0, WasiRights.SetAccessTime | WasiRights.SetWriteTimeNow)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFilestatGet(harness.Memory, fd, 0)));
        Assert.Equal(1_000_000_000UL, harness.ReadUInt64(40));
        Assert.Equal(WasiImplementation.ToTimestamp(time.GetUtcNow()), harness.ReadUInt64(48));

        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.FdFilestatSetTimes(fd, 0, 0, WasiRights.SetAccessTime | WasiRights.SetAccessTimeNow)));
        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.FdFilestatSetTimes(fd, 0, 0, WasiRights.SetWriteTime | WasiRights.SetWriteTimeNow)));
        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.FdFilestatSetTimes(fd, 0, 0, 16)));
        Assert.Equal(WasiErrno.Success, harness.PathCall("file.txt", (address, length) => harness.Wasi.PathFilestatSetTimes(harness.Memory, WasiHarness.Root, 0, address, length, 0, 2_000_000_000, WasiRights.SetWriteTime)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFilestatGet(harness.Memory, fd, 0)));
        Assert.Equal(2_000_000_000UL, harness.ReadUInt64(48));

        // The descriptor keeps its file: once the file is moved, the file at its old path is another file, whose times do not change
        files.Move("file.txt", "moved.txt");
        files.AddFile("file.txt", "another file"u8);
        Assert.Equal(WasiErrno.NoEntry, WasiHarness.Call(() => harness.Wasi.FdFilestatSetTimes(fd, 0, 3_000_000_000, WasiRights.SetWriteTime)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.FdFilestatGet(harness.Memory, fd, 0)));
        Assert.Equal(7UL, harness.ReadUInt64(32));
        Assert.NotEqual(3_000_000_000UL, files.GetStat("file.txt")!.Value.LastWriteTime.ToUnixTimeMilliseconds() * 1_000_000);
    }

    [Fact]
    public void RandomBytes_HaveTheRequestedLength()
    {
        var state = new Meziantou.Framework.WasmEngine.Wasi.Components.WasiComponentState(new WasiOptions());
        Assert.HasCount(2 * 1024 * 1024, state.GetRandomBytes(2 * 1024 * 1024));
        Assert.Throws<WasmTrapException>(() => state.GetRandomBytes((ulong)Meziantou.Framework.WasmEngine.Wasi.Components.WasiComponentState.MaxRandomBytes + 1));
    }

    [Fact]
    public void HardLinks_ShareTheFile()
    {
        var files = new WasiInMemoryFileSystem();
        files.AddFile("file.txt", "content"u8);
        using var harness = new WasiHarness(files);
        var (source, sourceLength) = harness.String("file.txt");
        var (link, linkLength) = harness.String("link.txt");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.PathLink(harness.Memory, WasiHarness.Root, 0, source, sourceLength, WasiHarness.Root, link, linkLength)));
        Assert.Equal(WasiErrno.Exists, WasiHarness.Call(() => harness.Wasi.PathLink(harness.Memory, WasiHarness.Root, 0, source, sourceLength, WasiHarness.Root, link, linkLength)));
        harness.Write(harness.Open("link.txt", Truncate), "changed");
        Assert.Equal("changed", Encoding.UTF8.GetString(files.ReadAllBytes("file.txt")));
        Assert.Equal(WasiErrno.Success, harness.PathCall("file.txt", (address, length) => harness.Wasi.PathFilestatGet(harness.Memory, WasiHarness.Root, 0, address, length, 0)));
        Assert.Equal(2UL, harness.ReadUInt64(24));

        using var temporaryDirectory = TemporaryDirectory.Create();
        temporaryDirectory.CreateTextFile("file.txt", "content");
        var hostFiles = new WasiHostFileSystem(temporaryDirectory.FullPath);
        using var host = new WasiHarness(hostFiles);
        (source, sourceLength) = host.String("file.txt");
        (link, linkLength) = host.String("link.txt");
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => host.Wasi.PathLink(host.Memory, WasiHarness.Root, 0, source, sourceLength, WasiHarness.Root, link, linkLength)));
        Assert.Equal(WasiErrno.Exists, WasiHarness.Call(() => host.Wasi.PathLink(host.Memory, WasiHarness.Root, 0, source, sourceLength, WasiHarness.Root, link, linkLength)));
        File.WriteAllText(temporaryDirectory.GetFullPath("link.txt"), "changed");
        Assert.Equal("changed", File.ReadAllText(temporaryDirectory.GetFullPath("file.txt")));

        // The two names are the same file: the same serial number, and two links
        var fileStat = hostFiles.GetStat("file.txt")!.Value;
        var linkStat = hostFiles.GetStat("link.txt")!.Value;
        Assert.Equal(fileStat.Inode, linkStat.Inode);
        Assert.Equal(2UL, fileStat.LinkCount);
        Assert.NotEqual(fileStat.Inode, hostFiles.GetStat("")!.Value.Inode);
        Assert.Equal(WasiErrno.NotPermitted, Assert.Throws<WasiException>(() => hostFiles.CreateHardLink("directory-link", "")).Errno);
    }

    [Fact]
    public void HardLinks_DoNotFollowTheSymbolicLinks()
    {
        // A hard link of a symbolic link to a file outside of the directory is a link to the symbolic link, not to the file
        using var outside = TemporaryDirectory.Create();
        outside.CreateTextFile("secret.txt", "secret");
        using var temporaryDirectory = TemporaryDirectory.Create();
        try
        {
            File.CreateSymbolicLink(temporaryDirectory.GetFullPath("escape"), outside.GetFullPath("secret.txt"));
        }
        catch (IOException)
        {
            global::Xunit.Assert.Skip("This machine does not allow creating symbolic links");
        }
        catch (UnauthorizedAccessException)
        {
            global::Xunit.Assert.Skip("This machine does not allow creating symbolic links");
        }

        var files = new WasiHostFileSystem(temporaryDirectory.FullPath);
        files.CreateHardLink("link", "escape");

        Assert.Equal(WasiFileType.SymbolicLink, files.GetStat("link")!.Value.Type);
        Assert.Equal(1UL, new WasiHostFileSystem(outside.FullPath).GetStat("secret.txt")!.Value.LinkCount);
    }

    [Fact]
    public void PollOneoff_ReportsTheFilesAndTheClocks()
    {
        using var harness = new WasiHarness(new WasiOptions());

        // A clock of 1 ms and the standard output: the output is ready, the clock did not expire
        WriteClockSubscription(harness.Memory, 0, userData: 1, timeout: 1_000_000);
        WriteFdSubscription(harness.Memory, 48, userData: 2, type: 2, fd: 1);
        WriteFdSubscription(harness.Memory, 96, userData: 3, type: 1, fd: 42);
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.PollOneoff(harness.Memory, 0, 1000, 3, 2000)));
        Assert.Equal(2u, harness.ReadUInt32(2000));
        Assert.Equal(2UL, harness.ReadUInt64(1000));
        Assert.Equal(WasiErrno.Success, (WasiErrno)BinaryPrimitives.ReadUInt16LittleEndian(harness.Memory.AsSpan(1008)));
        Assert.Equal(3UL, harness.ReadUInt64(1032));
        Assert.Equal(WasiErrno.BadFileDescriptor, (WasiErrno)BinaryPrimitives.ReadUInt16LittleEndian(harness.Memory.AsSpan(1040)));

        // Only a clock: the function waits for it
        WriteClockSubscription(harness.Memory, 0, userData: 7, timeout: 1_000_000);
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.PollOneoff(harness.Memory, 0, 1000, 1, 2000)));
        Assert.Equal(1u, harness.ReadUInt32(2000));
        Assert.Equal(7UL, harness.ReadUInt64(1000));
        Assert.Equal(0, harness.Memory[1010]);

        Assert.Equal(WasiErrno.InvalidArgument, WasiHarness.Call(() => harness.Wasi.PollOneoff(harness.Memory, 0, 1000, 0, 2000)));
        Assert.Equal(WasiErrno.Fault, WasiHarness.Call(() => harness.Wasi.PollOneoff(harness.Memory, 65000, 1000, 100, 2000)));
    }

    private static void WriteClockSubscription(byte[] memory, int address, ulong userData, ulong timeout)
    {
        memory.AsSpan(address, 48).Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(memory.AsSpan(address), userData);
        memory[address + 8] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(address + 16), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(memory.AsSpan(address + 24), timeout);
    }

    private static void WriteFdSubscription(byte[] memory, int address, ulong userData, byte type, uint fd)
    {
        memory.AsSpan(address, 48).Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(memory.AsSpan(address), userData);
        memory[address + 8] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(address + 16), fd);
    }

    [Fact]
    public void Sockets_AreNotSupported()
    {
        using var harness = new WasiHarness(new WasiOptions());
        Assert.Equal(WasiErrno.NotSocket, WasiHarness.Call(() => harness.Wasi.SockShutdown(1)));
        Assert.Equal(WasiErrno.BadFileDescriptor, WasiHarness.Call(() => harness.Wasi.SockShutdown(42)));
        Assert.Equal(WasiErrno.NotSocket, WasiHarness.Call(() => harness.Wasi.SockAccept(0)));
        Assert.Equal(WasiErrno.NotSocket, WasiHarness.Call(() => harness.Wasi.SockRecv(0)));
        Assert.Equal(WasiErrno.NotSocket, WasiHarness.Call(() => harness.Wasi.SockSend(0)));
        Assert.Equal(WasiErrno.NotImplemented, WasiHarness.Call(() => harness.Wasi.ProcRaise(9)));
        Assert.Equal(WasiErrno.Success, WasiHarness.Call(() => harness.Wasi.SchedYield()));
        Assert.Equal(5, Assert.Throws<WasiExitException>(() => harness.Wasi.ProcExit(5)).ExitCode);
    }

    [Fact]
    public void InMemoryFileSystem_HostApi()
    {
        var files = new WasiInMemoryFileSystem { MaxFileSize = 8 };
        files.AddFile("dir/file.txt", "content"u8);
        files.AddDirectory("other/nested");
        files.AddSymbolicLink("link", "dir/file.txt");
        Assert.True(files.Exists("dir"));
        Assert.True(files.Exists("other\\nested"));
        Assert.True(files.Exists("link"));
        Assert.False(files.Exists("missing"));
        Assert.Equal("content"u8.ToArray(), files.ReadAllBytes("/dir/file.txt"));
        Assert.Throws<FileNotFoundException>(() => files.ReadAllBytes("dir"));
        Assert.Throws<ArgumentException>(() => files.AddFile("dir/file.txt/child", "x"u8));
        Assert.Throws<ArgumentException>(() => files.AddFile("../x", "x"u8));
        Assert.Throws<ArgumentException>(() => files.AddSymbolicLink("link", "x"));

        // The files cannot grow beyond the maximum size
        using var harness = new WasiHarness(files);
        var fd = harness.Open("dir/file.txt", fdFlags: WasiRights.FdFlagAppend);
        Assert.Equal(1u, harness.Write(fd, "1"));
        Assert.Equal(WasiErrno.FileTooLarge, harness.TryWrite(fd, "2", out _));
        Assert.Equal(WasiErrno.FileTooLarge, WasiHarness.Call(() => harness.Wasi.FdFilestatSetSize(fd, 9)));
    }
}
