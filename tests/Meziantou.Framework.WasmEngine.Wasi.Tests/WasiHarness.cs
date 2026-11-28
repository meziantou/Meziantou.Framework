using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.WasmEngine.Wasi.Internal;

namespace Meziantou.Framework.WasmEngine.Wasi.Tests;

/// <summary>Calls the functions of WASI on a memory of 64 KiB, as a module would: the strings and the buffers are allocated in the memory.</summary>
internal sealed class WasiHarness : IDisposable
{
    public const ulong AllRights = (1UL << 30) - 1;
    public const int Root = 3;

    private readonly bool _ownsImplementation;
    private uint _next = 1024;

    public WasiHarness(WasiImplementation implementation)
    {
        Wasi = implementation;
    }

    public WasiHarness(WasiOptions options)
    {
        Wasi = new WasiImplementation(options);
        _ownsImplementation = true;
    }

    public WasiHarness(WasiFileSystem fileSystem, Stream? standardOutput = null)
        : this(new WasiOptions { PreopenedDirectories = [new WasiPreopenedDirectory("/", fileSystem)], StandardOutput = standardOutput })
    {
    }

    public byte[] Memory { get; } = new byte[65536];

    public WasiImplementation Wasi { get; }

    /// <summary>Calls a function of the implementation, and returns the error code of the exception it throws, as the imports of WASI do.</summary>
    public static WasiErrno Call(Func<WasiErrno> call)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (WasiErrors.TryGetErrno(ex, out var errno))
        {
            return errno;
        }
    }

    public uint Allocate(int size)
    {
        var address = _next;
        _next += (uint)((size + 7) & ~7);
        if (_next > Memory.Length)
        {
            _next = 1024;
            address = _next;
            _next += (uint)((size + 7) & ~7);
        }

        return address;
    }

    public (uint Address, uint Length) String(string value) => Bytes(Encoding.UTF8.GetBytes(value));

    public (uint Address, uint Length) Bytes(ReadOnlySpan<byte> value)
    {
        var address = Allocate(value.Length);
        value.CopyTo(Memory.AsSpan((int)address));
        return (address, (uint)value.Length);
    }

    public uint ReadUInt32(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(Memory.AsSpan((int)address));

    public ulong ReadUInt64(uint address) => BinaryPrimitives.ReadUInt64LittleEndian(Memory.AsSpan((int)address));

    public WasiErrno TryOpen(string path, out int fd, ushort openFlags = 0, ulong rights = AllRights, ushort fdFlags = 0, uint lookupFlags = 1, int directory = Root)
    {
        // A directory cannot be opened with the right to write: the default rights of a directory are the rights of a directory
        if ((openFlags & WasiRights.OpenDirectory) != 0 && rights == AllRights)
        {
            rights = WasiRights.Directory;
        }

        var (address, length) = String(path);
        var result = Allocate(4);
        var errno = Call(() => Wasi.PathOpen(Memory, directory, lookupFlags, address, length, openFlags, rights, AllRights, fdFlags, result));
        fd = errno == WasiErrno.Success ? (int)ReadUInt32(result) : -1;
        return errno;
    }

    public int Open(string path, ushort openFlags = 0, ulong rights = AllRights, ushort fdFlags = 0, uint lookupFlags = 1, int directory = Root)
    {
        Assert.Equal(WasiErrno.Success, TryOpen(path, out var fd, openFlags, rights, fdFlags, lookupFlags, directory));
        return fd;
    }

    public void CreateDirectory(string path) => Assert.Equal(WasiErrno.Success, PathCall(path, (address, length) => Wasi.PathCreateDirectory(Memory, Root, address, length)));

    public int Create(string path, string content)
    {
        var fd = Open(path, WasiRights.OpenCreate | WasiRights.OpenTruncate);
        Assert.Equal((uint)Encoding.UTF8.GetByteCount(content), Write(fd, content));
        return fd;
    }

    public WasiErrno TryWrite(int fd, string text, out uint written)
    {
        var (address, length) = String(text);
        var iovec = Allocate(8);
        BinaryPrimitives.WriteUInt32LittleEndian(Memory.AsSpan((int)iovec), address);
        BinaryPrimitives.WriteUInt32LittleEndian(Memory.AsSpan((int)iovec + 4), length);
        var result = Allocate(4);
        var errno = Call(() => Wasi.FdWrite(Memory, fd, iovec, 1, result));
        written = errno == WasiErrno.Success ? ReadUInt32(result) : 0;
        return errno;
    }

    public uint Write(int fd, string text)
    {
        Assert.Equal(WasiErrno.Success, TryWrite(fd, text, out var written));
        return written;
    }

    public WasiErrno TryRead(int fd, int count, out string text)
    {
        var buffer = Allocate(count);
        var iovec = Allocate(8);
        BinaryPrimitives.WriteUInt32LittleEndian(Memory.AsSpan((int)iovec), buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(Memory.AsSpan((int)iovec + 4), (uint)count);
        var result = Allocate(4);
        var errno = Call(() => Wasi.FdRead(Memory, fd, iovec, 1, result));
        text = errno == WasiErrno.Success ? Encoding.UTF8.GetString(Memory, (int)buffer, (int)ReadUInt32(result)) : "";
        return errno;
    }

    public string Read(int fd, int count = 256)
    {
        Assert.Equal(WasiErrno.Success, TryRead(fd, count, out var text));
        return text;
    }

    public WasiErrno PathCall(string path, Func<uint, uint, WasiErrno> call)
    {
        var (address, length) = String(path);
        return Call(() => call(address, length));
    }

    public WasiFileType GetFileType(string path, bool follow = false)
    {
        var (address, length) = String(path);
        var stat = Allocate(64);
        Assert.Equal(WasiErrno.Success, Call(() => Wasi.PathFilestatGet(Memory, Root, follow ? 1u : 0u, address, length, stat)));
        return (WasiFileType)Memory[stat + 16];
    }

    /// <summary>Returns the names of the entries of a directory, with a buffer of <paramref name="bufferSize"/> bytes per call.</summary>
    public List<string> ReadDirectory(int fd, int bufferSize = 256)
    {
        var names = new List<string>();
        var cookie = 0UL;
        while (true)
        {
            var buffer = Allocate(bufferSize);
            var used = Allocate(4);
            Assert.Equal(WasiErrno.Success, Call(() => Wasi.FdReaddir(Memory, fd, buffer, (uint)bufferSize, cookie, used)));
            var length = (int)ReadUInt32(used);
            var offset = 0;
            var parsed = false;
            while (offset + 24 <= length)
            {
                var next = ReadUInt64(buffer + (uint)offset);
                var nameLength = (int)ReadUInt32(buffer + (uint)offset + 16);
                if (offset + 24 + nameLength > length)
                    break;

                names.Add(Encoding.UTF8.GetString(Memory, (int)buffer + offset + 24, nameLength));
                cookie = next;
                offset += 24 + nameLength;
                parsed = true;
            }

            if (length < bufferSize)
                return names;

            Assert.True(parsed, "The buffer is too small for an entry");
        }
    }

    public void Dispose()
    {
        if (_ownsImplementation)
        {
            Wasi.Dispose();
        }
    }
}
