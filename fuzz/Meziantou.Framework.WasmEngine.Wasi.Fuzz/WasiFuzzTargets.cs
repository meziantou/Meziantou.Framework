using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WasmEngine.Wasi.Internal;

namespace Meziantou.Framework.WasmEngine.Wasi.Fuzz;

/// <summary>
/// The targets of WASI Preview 1 (and of WASI 0.2 and 0.3: <see cref="WasiComponentFuzzTargets"/>):
/// <list type="bullet">
/// <item>The resolution of the paths of the module, with the symbolic links of the input, gives a canonical path inside
/// the directory of the descriptor, or fails with an error code of WASI.</item>
/// <item>The functions called with the arguments of the input, on a memory and a file system of the input, return an
/// error code of WASI or end the program (<c>proc_exit</c>): they never throw another exception, never give the file
/// system a path that is not canonical, never change the files outside of the preopened directory, and report sizes
/// that fit the buffers of the module.</item>
/// </list>
/// </summary>
public static class WasiFuzzTargets
{
    private const int MemorySize = 64 * 1024;
    private const int MaxCalls = 256;

    // The segments of the paths: the separators, the parents, the names of the files and links of the targets, and the names reserved by Windows
    private static readonly string[] PathTokens = ["/", "..", ".", "//", "../", "./", "\\", ":", "\0", "dir", "file.txt", "link", "loop", "escape", "parent", "secret.txt", "jail", "CON", "NUL", "COM1", "a.", "=", "\n", "é", "�"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("wasi-path", PathTarget) { Seeds = GetPathSeeds, Dictionary = PathTokens },
        new("wasi-calls", CallsTarget) { Seeds = GetCallSeeds, Dictionary = PathTokens },
        .. WasiComponentFuzzTargets.All,
        .. WasiNetworkFuzzTargets.All,
    ];

    // ------------------------------------------------------------------------------------------------------------
    // wasi-path
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The input is a byte of options (bit 0: follow the last link; bit 1: resolve from the directory <c>dir</c>) and
    /// lines: <c>name=target</c> adds a link, <c>name/</c> a directory, <c>name</c> a file, and the last line is the path.
    /// </summary>
    private static void PathTarget(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        var options = data[0];
        var lines = FuzzText.Decode(data[1..]).Split('\n');
        var fileSystem = CreateFileSystem();
        for (var i = 0; i < lines.Length - 1; i++)
        {
            AddEntry(fileSystem, lines[i]);
        }

        var directoryPath = (options & 2) != 0 ? "dir" : "";
        if (directoryPath.Length > 0 && fileSystem.GetStat(directoryPath) is not { Type: WasiFileType.Directory })
            return;

        var directory = new WasiDirectoryFd(fileSystem, directoryPath, preopenName: null, WasiRights.Directory, WasiRights.DirectoryInheriting);
        var path = lines[^1];
        string resolved;
        try
        {
            (resolved, _) = WasiImplementation.Resolve(directory, path, followFinalLink: (options & 1) != 0);
        }
        catch (WasiException ex)
        {
            FuzzAssert.True(Enum.IsDefined(ex.Errno) && ex.Errno != WasiErrno.Success, "The resolution failed with an unknown code: " + FuzzAssert.Format(ex.Errno));
            return;
        }

        FuzzAssert.True(WasiPaths.IsCanonical(resolved), "The resolved path is not canonical: " + FuzzAssert.Format(resolved));
        FuzzAssert.True(WasiPaths.IsSameOrInside(resolved, directoryPath), "The resolved path " + FuzzAssert.Format(resolved) + " is outside of the directory " + FuzzAssert.Format(directoryPath));
        if (resolved.Length > 0)
        {
            FuzzAssert.True(fileSystem.GetStat(WasiPaths.GetParent(resolved)) is { Type: WasiFileType.Directory }, "The parent of the resolved path " + FuzzAssert.Format(resolved) + " is not a directory");
        }
    }

    private static CheckedFileSystem CreateFileSystem()
    {
        var fileSystem = new CheckedFileSystem();
        fileSystem.Add("dir", linkTarget: null, isDirectory: true);
        fileSystem.Add("dir/file.txt", linkTarget: null, isDirectory: false);
        fileSystem.Add("file.txt", linkTarget: null, isDirectory: false);
        return fileSystem;
    }

    private static void AddEntry(CheckedFileSystem fileSystem, string line)
    {
        var index = line.IndexOf('=', StringComparison.Ordinal);
        if (index > 0)
        {
            fileSystem.Add(line[..index], line[(index + 1)..], isDirectory: false);
        }
        else if (line.EndsWith('/', StringComparison.Ordinal))
        {
            fileSystem.Add(line.TrimEnd('/'), linkTarget: null, isDirectory: true);
        }
        else if (line.Length > 0)
        {
            fileSystem.Add(line, linkTarget: null, isDirectory: false);
        }
    }

    private static IEnumerable<FuzzSeed> GetPathSeeds()
    {
        // The paths of the unit tests of the resolution
        (byte Options, string Text)[] seeds =
        [
            (1, "file.txt"),
            (1, "dir/../dir/./file.txt"),
            (1, "../secret.txt"),
            (1, "dir/../../secret.txt"),
            (1, "/secret.txt"),
            (3, "../file.txt"),
            (0, "file.txt/"),
            (1, "link=dir/file.txt\nlink"),
            (0, "link=dir/file.txt\nlink"),
            (1, "dirlink=dir\ndirlink/file.txt"),
            (0, "dirlink=dir\ndirlink/"),
            (1, "escape=../secret.txt\nescape"),
            (3, "escape=../../secret.txt\ndir/escape\nescape"),
            (1, "dir/parent=..\ndir/parent/dir/file.txt"),
            (1, "dir/parent=..\ndir/parent/../secret.txt"),
            (1, "absolute=/dir/file.txt\nabsolute"),
            (1, "loop1=loop2\nloop2=loop1\nloop1"),
            (1, "dangling=missing\ndangling"),
            (1, "new/\nnew/./"),
        ];

        foreach (var (options, text) in seeds)
        {
            yield return new FuzzSeed(text.Replace('\n', '_').Replace('/', '-'), (byte[])[options, .. FuzzText.Encode(text)]);
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi-calls
    // ------------------------------------------------------------------------------------------------------------

    private enum Call : byte
    {
        ArgsGet,
        ArgsSizesGet,
        EnvironGet,
        EnvironSizesGet,
        ClockResGet,
        ClockTimeGet,
        FdAdvise,
        FdAllocate,
        FdClose,
        FdDatasync,
        FdFdstatGet,
        FdFdstatSetFlags,
        FdFdstatSetRights,
        FdFilestatGet,
        FdFilestatSetSize,
        FdFilestatSetTimes,
        FdPread,
        FdPrestatGet,
        FdPrestatDirName,
        FdPwrite,
        FdRead,
        FdReaddir,
        FdRenumber,
        FdSeek,
        FdSync,
        FdTell,
        FdWrite,
        PathCreateDirectory,
        PathFilestatGet,
        PathFilestatSetTimes,
        PathLink,
        PathOpen,
        PathReadlink,
        PathRemoveDirectory,
        PathRename,
        PathSymlink,
        PathUnlinkFile,
        PollOneoff,
        RandomGet,
        SchedYield,
        ProcExit,
        SockShutdown,
    }

    private const int CallCount = (int)Call.SockShutdown + 1;

    /// <summary>
    /// The input is the initial bytes of the memory (a 16-bit length and the bytes), then calls: the function (a byte),
    /// and its arguments. The descriptors are bytes (-1 to 18), the pointers 16-bit, the lengths 8-bit, the 64-bit values 64-bit.
    /// </summary>
    private static void CallsTarget(ReadOnlySpan<byte> data)
    {
        var reader = new CallReader(data);
        var memory = new byte[MemorySize];
        var length = Math.Min(reader.U16(), MemorySize);
        reader.Bytes(length).CopyTo(memory);

        var fileSystem = CreateFileSystem();
        using var output = new MemoryStream();
        using var input = new MemoryStream("input"u8.ToArray());
        using var wasi = new WasiImplementation(new WasiOptions
        {
            Arguments = ["program", "argument"],
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal) { ["KEY"] = "value" },
            StandardInput = input,
            StandardOutput = output,
            PreopenedDirectories = [new WasiPreopenedDirectory("/", fileSystem), new WasiPreopenedDirectory("/other", new WasiInMemoryFileSystem { MaxFileSize = 4096 })],
            TimeProvider = InstantTimeProvider.Instance,
            RandomSource = buffer => buffer.Fill(1),
        });

        for (var i = 0; i < MaxCalls && !reader.End; i++)
        {
            var call = (Call)(reader.U8() % CallCount);
            WasiErrno errno;
            try
            {
                errno = Invoke(wasi, memory, call, ref reader);
            }
            catch (WasiExitException)
            {
                FuzzAssert.True(call == Call.ProcExit, "A function other than proc_exit ended the program: " + call.ToString());
                break;
            }
            catch (WasiException ex)
            {
                errno = ex.Errno;
            }

            FuzzAssert.True(Enum.IsDefined(errno), call.ToString() + " returned an unknown code: " + FuzzAssert.Format(errno));
            FuzzAssert.True(wasi.FileDescriptorCount <= WasiImplementation.MaxFileDescriptors, "There are too many file descriptors");
        }

        fileSystem.CheckSecret();
    }

    private static WasiErrno Invoke(WasiImplementation wasi, byte[] memory, Call call, ref CallReader reader)
    {
        switch (call)
        {
            case Call.ArgsGet:
                return wasi.ArgsGet(memory, reader.Pointer(), reader.Pointer());

            case Call.ArgsSizesGet:
                return wasi.ArgsSizesGet(memory, reader.Pointer(), reader.Pointer());

            case Call.EnvironGet:
                return wasi.EnvironGet(memory, reader.Pointer(), reader.Pointer());

            case Call.EnvironSizesGet:
                return wasi.EnvironSizesGet(memory, reader.Pointer(), reader.Pointer());

            case Call.ClockResGet:
                return wasi.ClockResGet(memory, reader.U8(), reader.Pointer());

            case Call.ClockTimeGet:
                return wasi.ClockTimeGet(memory, reader.U8(), reader.U64(), reader.Pointer());

            case Call.FdAdvise:
                return wasi.FdAdvise(reader.Fd(), reader.U64(), reader.U64(), reader.U8());

            case Call.FdAllocate:
                return wasi.FdAllocate(reader.Fd(), reader.U32(), reader.U32());

            case Call.FdClose:
                return wasi.FdClose(reader.Fd());

            case Call.FdDatasync:
                return wasi.FdDatasync(reader.Fd());

            case Call.FdFdstatGet:
                return wasi.FdFdstatGet(memory, reader.Fd(), reader.Pointer());

            case Call.FdFdstatSetFlags:
                return wasi.FdFdstatSetFlags(reader.Fd(), reader.U16());

            case Call.FdFdstatSetRights:
                return wasi.FdFdstatSetRights(reader.Fd(), reader.U64(), reader.U64());

            case Call.FdFilestatGet:
                return wasi.FdFilestatGet(memory, reader.Fd(), reader.Pointer());

            case Call.FdFilestatSetSize:
                return wasi.FdFilestatSetSize(reader.Fd(), reader.U32());

            case Call.FdFilestatSetTimes:
                return wasi.FdFilestatSetTimes(reader.Fd(), reader.U64(), reader.U64(), reader.U16());

            case Call.FdPread:
                return wasi.FdPread(memory, reader.Fd(), reader.Pointer(), reader.Length(), reader.U32(), reader.Pointer());

            case Call.FdPrestatGet:
                return wasi.FdPrestatGet(memory, reader.Fd(), reader.Pointer());

            case Call.FdPrestatDirName:
                return wasi.FdPrestatDirName(memory, reader.Fd(), reader.Pointer(), reader.Length());

            case Call.FdPwrite:
                return wasi.FdPwrite(memory, reader.Fd(), reader.Pointer(), reader.Length(), reader.U32(), reader.Pointer());

            case Call.FdRead:
                return wasi.FdRead(memory, reader.Fd(), reader.Pointer(), reader.Length(), reader.Pointer());

            case Call.FdReaddir:
            {
                var buffer = reader.Pointer();
                var bufferLength = (uint)reader.U16();
                var used = reader.Pointer();
                var errno = wasi.FdReaddir(memory, reader.Fd(), buffer, bufferLength, reader.U8(), used);
                if (errno == WasiErrno.Success)
                {
                    FuzzAssert.True(BinaryPrimitives.ReadUInt32LittleEndian(memory.AsSpan((int)used)) <= bufferLength || OverlapsResult(buffer, bufferLength, used), "fd_readdir used more than the buffer");
                }

                return errno;
            }

            case Call.FdRenumber:
                return wasi.FdRenumber(reader.Fd(), reader.Fd());

            case Call.FdSeek:
            {
                var fd = reader.Fd();
                var position = reader.Pointer();
                var errno = wasi.FdSeek(memory, fd, (int)reader.U32(), reader.U8(), position);
                if (errno == WasiErrno.Success)
                {
                    var expected = BinaryPrimitives.ReadUInt64LittleEndian(memory.AsSpan((int)position));
                    FuzzAssert.Equal(WasiErrno.Success, wasi.FdTell(memory, fd, position), "fd_tell after fd_seek");
                    FuzzAssert.Equal(expected, BinaryPrimitives.ReadUInt64LittleEndian(memory.AsSpan((int)position)), "fd_tell after fd_seek");
                }

                return errno;
            }

            case Call.FdSync:
                return wasi.FdSync(reader.Fd());

            case Call.FdTell:
                return wasi.FdTell(memory, reader.Fd(), reader.Pointer());

            case Call.FdWrite:
                return wasi.FdWrite(memory, reader.Fd(), reader.Pointer(), reader.Length(), reader.Pointer());

            case Call.PathCreateDirectory:
                return wasi.PathCreateDirectory(memory, reader.Fd(), reader.Pointer(), reader.Length());

            case Call.PathFilestatGet:
                return wasi.PathFilestatGet(memory, reader.Fd(), reader.U8(), reader.Pointer(), reader.Length(), reader.Pointer());

            case Call.PathFilestatSetTimes:
                return wasi.PathFilestatSetTimes(memory, reader.Fd(), reader.U8(), reader.Pointer(), reader.Length(), reader.U64(), reader.U64(), reader.U16());

            case Call.PathLink:
                return wasi.PathLink(memory, reader.Fd(), reader.U8(), reader.Pointer(), reader.Length(), reader.Fd(), reader.Pointer(), reader.Length());

            case Call.PathOpen:
                return wasi.PathOpen(memory, reader.Fd(), reader.U8(), reader.Pointer(), reader.Length(), reader.U16(), reader.U64(), reader.U64(), reader.U16(), reader.Pointer());

            case Call.PathReadlink:
            {
                var fd = reader.Fd();
                var path = reader.Pointer();
                var pathLength = reader.Length();
                var buffer = reader.Pointer();
                var bufferLength = reader.Length();
                var used = reader.Pointer();
                var errno = wasi.PathReadlink(memory, fd, path, pathLength, buffer, bufferLength, used);
                if (errno == WasiErrno.Success)
                {
                    FuzzAssert.True(BinaryPrimitives.ReadUInt32LittleEndian(memory.AsSpan((int)used)) <= bufferLength || OverlapsResult(buffer, bufferLength, used), "path_readlink used more than the buffer");
                }

                return errno;
            }

            case Call.PathRemoveDirectory:
                return wasi.PathRemoveDirectory(memory, reader.Fd(), reader.Pointer(), reader.Length());

            case Call.PathRename:
                return wasi.PathRename(memory, reader.Fd(), reader.Pointer(), reader.Length(), reader.Fd(), reader.Pointer(), reader.Length());

            case Call.PathSymlink:
                return wasi.PathSymlink(memory, reader.Pointer(), reader.Length(), reader.Fd(), reader.Pointer(), reader.Length());

            case Call.PathUnlinkFile:
                return wasi.PathUnlinkFile(memory, reader.Fd(), reader.Pointer(), reader.Length());

            case Call.PollOneoff:
                return wasi.PollOneoff(memory, reader.Pointer(), reader.Pointer(), (uint)(reader.U8() % 8), reader.Pointer());

            case Call.RandomGet:
                return wasi.RandomGet(memory, reader.Pointer(), reader.Length());

            case Call.SchedYield:
                return wasi.SchedYield();

            case Call.ProcExit:
                wasi.ProcExit(reader.U8());
                return WasiErrno.Success;

            default:
                return wasi.SockShutdown(reader.Fd());
        }
    }

    // The buffer can contain the address of the result: the result is written after the buffer
    private static bool OverlapsResult(uint buffer, uint length, uint result) => result + 4 > buffer && result < buffer + length;

    private ref struct CallReader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _data = data;

        public readonly bool End => _data.IsEmpty;

        public ReadOnlySpan<byte> Bytes(int count)
        {
            var bytes = _data[..Math.Min(count, _data.Length)];
            _data = _data[bytes.Length..];
            return bytes;
        }

        public byte U8() => Bytes(1) is [var value] ? value : (byte)0;

        public ushort U16() => Bytes(2) is { Length: 2 } bytes ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : (ushort)0;

        public uint U32() => Bytes(4) is { Length: 4 } bytes ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : 0;

        public ulong U64() => Bytes(8) is { Length: 8 } bytes ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : 0;

        public int Fd() => (U8() % 20) - 1;

        public uint Pointer() => U16();

        public uint Length() => U8();
    }

    /// <summary>Writes the inputs of <see cref="CallsTarget"/>.</summary>
    private sealed class CallWriter
    {
        private readonly List<byte> _calls = [];
        private readonly byte[] _memory = new byte[2048];
        private int _next = 1024;

        public (ushort Address, byte Length) String(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var address = _next;
            bytes.CopyTo(_memory, address);
            _next += bytes.Length + 1;
            return ((ushort)address, (byte)bytes.Length);
        }

        /// <summary>Writes an iovec at <paramref name="address"/> for the buffer.</summary>
        public ushort Iovec(ushort address, ushort buffer, uint length)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_memory.AsSpan(address), buffer);
            BinaryPrimitives.WriteUInt32LittleEndian(_memory.AsSpan(address + 4), length);
            return address;
        }

        public CallWriter Call(Call call, params object[] arguments)
        {
            _calls.Add((byte)call);
            foreach (var argument in arguments)
            {
                switch (argument)
                {
                    case Fd fd:
                        _calls.Add((byte)(fd.Value + 1));
                        break;

                    case byte value:
                        _calls.Add(value);
                        break;

                    case ushort value:
                        _calls.Add((byte)value);
                        _calls.Add((byte)(value >> 8));
                        break;

                    case uint value:
                        var u32 = new byte[4];
                        BinaryPrimitives.WriteUInt32LittleEndian(u32, value);
                        _calls.AddRange(u32);
                        break;

                    case ulong value:
                        var u64 = new byte[8];
                        BinaryPrimitives.WriteUInt64LittleEndian(u64, value);
                        _calls.AddRange(u64);
                        break;

                    default:
                        throw new ArgumentException("Unsupported argument " + argument, nameof(arguments));
                }
            }

            return this;
        }

        public byte[] ToArray() => [(byte)_memory.Length, (byte)(_memory.Length >> 8), .. _memory, .. _calls];
    }

    private readonly record struct Fd(int Value);

    private static IEnumerable<FuzzSeed> GetCallSeeds()
    {
        const ulong AllRights = (1UL << 30) - 1;
        var root = new Fd(3);

        // Write a file, read it back, seek, tell, stat (as the tests of the files)
        var files = new CallWriter();
        var (name, nameLength) = files.String("dir/new.txt");
        var (text, textLength) = files.String("Hello, world");
        files.Iovec(16, text, textLength);
        files.Iovec(32, 512, 64);
        files
            .Call(Call.PathOpen, root, (byte)1, name, nameLength, (ushort)(WasiRights.OpenCreate | WasiRights.OpenTruncate), AllRights, AllRights, (ushort)0, (ushort)64)
            .Call(Call.FdWrite, new Fd(5), (ushort)16, (byte)1, (ushort)80)
            .Call(Call.FdSeek, new Fd(5), 0u, (byte)0, (ushort)88)
            .Call(Call.FdRead, new Fd(5), (ushort)32, (byte)1, (ushort)80)
            .Call(Call.FdTell, new Fd(5), (ushort)88)
            .Call(Call.FdFilestatGet, new Fd(5), (ushort)128)
            .Call(Call.FdPwrite, new Fd(5), (ushort)16, (byte)1, 3u, (ushort)80)
            .Call(Call.FdPread, new Fd(5), (ushort)32, (byte)1, 0u, (ushort)80)
            .Call(Call.FdFilestatSetSize, new Fd(5), 4u)
            .Call(Call.FdAllocate, new Fd(5), 0u, 100u)
            .Call(Call.FdClose, new Fd(5));
        yield return new FuzzSeed("files", files.ToArray());

        // Directories: create, list, rename, remove
        var directories = new CallWriter();
        var (directory, directoryLength) = directories.String("new");
        var (renamed, renamedLength) = directories.String("renamed");
        var (dot, dotLength) = directories.String(".");
        directories
            .Call(Call.PathCreateDirectory, root, directory, directoryLength)
            .Call(Call.PathOpen, root, (byte)1, dot, dotLength, (ushort)WasiRights.OpenDirectory, WasiRights.Directory, AllRights, (ushort)0, (ushort)64)
            .Call(Call.FdReaddir, (ushort)256, (ushort)512, (ushort)68, new Fd(5), (byte)0)
            .Call(Call.PathRename, root, directory, directoryLength, root, renamed, renamedLength)
            .Call(Call.PathRemoveDirectory, root, renamed, renamedLength)
            .Call(Call.PathRemoveDirectory, root, dot, dotLength);
        yield return new FuzzSeed("directories", directories.ToArray());

        // Links that escape the directory, a loop, and the links of the tests of the links
        var links = new CallWriter();
        var (escape, escapeLength) = links.String("../secret.txt");
        var (link, linkLength) = links.String("link");
        var (loop, loopLength) = links.String("loop");
        var (target, targetLength) = links.String("dir/file.txt");
        var (hardLink, hardLinkLength) = links.String("hard.txt");
        links
            .Call(Call.PathSymlink, escape, escapeLength, root, link, linkLength)
            .Call(Call.PathOpen, root, (byte)1, link, linkLength, (ushort)0, AllRights, AllRights, (ushort)0, (ushort)64)
            .Call(Call.PathOpen, root, (byte)1, escape, escapeLength, (ushort)0, AllRights, AllRights, (ushort)0, (ushort)64)
            .Call(Call.PathReadlink, root, link, linkLength, (ushort)256, (byte)64, (ushort)80)
            .Call(Call.PathUnlinkFile, root, link, linkLength)
            .Call(Call.PathSymlink, loop, loopLength, root, loop, loopLength)
            .Call(Call.PathFilestatGet, root, (byte)1, loop, loopLength, (ushort)128)
            .Call(Call.PathSymlink, target, targetLength, root, link, linkLength)
            .Call(Call.PathFilestatGet, root, (byte)1, link, linkLength, (ushort)128)
            .Call(Call.PathLink, root, (byte)0, target, targetLength, root, hardLink, hardLinkLength)
            .Call(Call.PathRename, root, hardLink, hardLinkLength, root, escape, escapeLength);
        yield return new FuzzSeed("links", links.ToArray());

        // The standard streams, the arguments, the clocks, the random numbers, the rights, poll_oneoff and the exit
        var process = new CallWriter();
        var (message, messageLength) = process.String("output");
        process.Iovec(16, message, messageLength);
        process.Iovec(32, 512, 16);
        process
            .Call(Call.FdWrite, new Fd(1), (ushort)16, (byte)1, (ushort)80)
            .Call(Call.FdRead, new Fd(0), (ushort)32, (byte)1, (ushort)80)
            .Call(Call.FdWrite, new Fd(0), (ushort)16, (byte)1, (ushort)80)
            .Call(Call.ArgsSizesGet, (ushort)64, (ushort)68)
            .Call(Call.ArgsGet, (ushort)128, (ushort)256)
            .Call(Call.EnvironGet, (ushort)128, (ushort)256)
            .Call(Call.ClockTimeGet, (byte)1, 0UL, (ushort)64)
            .Call(Call.ClockResGet, (byte)0, (ushort)64)
            .Call(Call.RandomGet, (ushort)512, (byte)32)
            .Call(Call.FdPrestatGet, root, (ushort)64)
            .Call(Call.FdPrestatDirName, new Fd(4), (ushort)512, (byte)6)
            .Call(Call.FdFdstatGet, root, (ushort)64)
            .Call(Call.FdFdstatSetRights, root, WasiRights.Directory, 0UL)
            .Call(Call.PollOneoff, (ushort)640, (ushort)768, (byte)2, (ushort)80)
            .Call(Call.FdRenumber, new Fd(4), root)
            .Call(Call.SockShutdown, new Fd(1))
            .Call(Call.ProcExit, (byte)3);
        yield return new FuzzSeed("process", process.ToArray());
    }
}
