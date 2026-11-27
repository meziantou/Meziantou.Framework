using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WasmEngine.Wasi.Components;

namespace Meziantou.Framework.WasmEngine.Wasi.Fuzz;

/// <summary>
/// The targets of WASI 0.2 and 0.3: the operations of the descriptors of <c>wasi:filesystem</c>, called with the
/// arguments of the input on a file system of the input, return a value or fail with an error that the interfaces convert
/// to an <c>error-code</c>: they never throw another exception, never give the file system a path that is not canonical,
/// never change the files outside of the preopened directory, and read no more bytes than asked.
/// </summary>
internal static class WasiComponentFuzzTargets
{
    private const int MaxOperations = 128;

    private static readonly string[] Tokens = ["\n", "/", "..", ".", "//", "../", "./", "\\", "\0", "dir", "file.txt", "link", "secret.txt", "new", "CON", "a.", "é"];

    public static IEnumerable<FuzzTarget> All =>
    [
        new("wasi-component-files", FilesTarget) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    private enum Operation
    {
        OpenAt,
        Stat,
        StatAt,
        CreateDirectoryAt,
        RemoveDirectoryAt,
        UnlinkFileAt,
        RenameAt,
        SymlinkAt,
        ReadlinkAt,
        LinkAt,
        Read,
        Write,
        SetSize,
        ReadDirectory,
        IsSameObject,
        Close,
        Count,
    }

    /// <summary>
    /// The input is a sequence of operations: a byte of the operation, a byte of the descriptor (an index in the open
    /// descriptors, the preopened directory first), a byte of flags, then the paths (or the text) each ending with a new line.
    /// </summary>
    private static void FilesTarget(ReadOnlySpan<byte> data)
    {
        var fileSystem = new CheckedFileSystem();
        fileSystem.Add("dir", linkTarget: null, isDirectory: true);
        fileSystem.Add("dir/file.txt", linkTarget: null, isDirectory: false);
        fileSystem.Add("file.txt", linkTarget: null, isDirectory: false);
        fileSystem.Add("link", "dir/file.txt", isDirectory: false);
        var descriptors = new List<WasiFileDescriptor> { new(fileSystem, "", isDirectory: true, stream: null, canRead: true, canWrite: false, canMutate: true) };
        for (var count = 0; count < MaxOperations && !data.IsEmpty; count++)
        {
            var operation = (Operation)(FuzzInput.ReadByte(ref data) % (int)Operation.Count);
            var descriptor = descriptors[FuzzInput.ReadByte(ref data) % descriptors.Count];
            var other = descriptors[FuzzInput.ReadByte(ref data) % descriptors.Count];
            var flags = FuzzInput.ReadByte(ref data);
            var path = ReadText(ref data);
            try
            {
                Run(operation, descriptor, other, flags, path, ref data, descriptors);
            }
            catch (Exception ex) when (ex is not FuzzAssertionException)
            {
                FuzzAssert.True(WasiComponentFiles.IsError(ex), $"{operation} threw an exception that is not an error of WASI: {ex.GetType().Name}: {ex.Message}");
            }
        }

        foreach (var descriptor in descriptors)
        {
            descriptor.Close();
        }
    }

    private static string ReadText(ref ReadOnlySpan<byte> data) => FuzzText.Decode(FuzzInput.ReadUntil(ref data, (byte)'\n'));

    private static void Run(Operation operation, WasiFileDescriptor descriptor, WasiFileDescriptor other, byte flags, string path, ref ReadOnlySpan<byte> data, List<WasiFileDescriptor> descriptors)
    {
        var follow = (flags & 1) != 0;
        switch (operation)
        {
            case Operation.OpenAt:
                var opened = WasiComponentFiles.OpenAt(descriptor, follow, path, create: (flags & 2) != 0, mustBeDirectory: (flags & 4) != 0, exclusive: (flags & 8) != 0, truncate: (flags & 16) != 0, read: (flags & 32) != 0, write: (flags & 64) != 0, mutate: (flags & 128) != 0);
                FuzzAssert.True(opened.IsDirectory || opened.CanRead || opened.CanWrite, "A file is opened without read and write");
                descriptors.Add(opened);
                break;

            case Operation.Stat:
                WasiComponentFiles.Stat(descriptor);
                break;

            case Operation.StatAt:
                WasiComponentFiles.StatAt(descriptor, follow, path);
                break;

            case Operation.CreateDirectoryAt:
                WasiComponentFiles.CreateDirectoryAt(descriptor, path);
                break;

            case Operation.RemoveDirectoryAt:
                WasiComponentFiles.RemoveDirectoryAt(descriptor, path);
                break;

            case Operation.UnlinkFileAt:
                WasiComponentFiles.UnlinkFileAt(descriptor, path);
                break;

            case Operation.RenameAt:
                WasiComponentFiles.RenameAt(descriptor, path, other, ReadText(ref data));
                break;

            case Operation.SymlinkAt:
                WasiComponentFiles.SymlinkAt(descriptor, path, ReadText(ref data));
                break;

            case Operation.ReadlinkAt:
                WasiComponentFiles.ReadlinkAt(descriptor, path);
                break;

            case Operation.LinkAt:
                WasiComponentFiles.LinkAt(descriptor, follow, path, other, ReadText(ref data));
                break;

            case Operation.Read:
                var length = (ulong)flags * 3;
                var (bytes, _) = WasiComponentFiles.Read(descriptor, length, (ulong)path.Length);
                FuzzAssert.True((ulong)bytes.Length <= length, $"Read returned {bytes.Length} bytes for {length} asked");
                break;

            case Operation.Write:
                var written = WasiComponentFiles.Write(descriptor, Encoding.UTF8.GetBytes(path), flags);
                FuzzAssert.True(written <= (ulong)Encoding.UTF8.GetByteCount(path), "Write wrote more bytes than given");
                break;

            case Operation.SetSize:
                WasiComponentFiles.SetSize(descriptor, (ulong)flags * 100);
                break;

            case Operation.ReadDirectory:
                foreach (var entry in WasiComponentFiles.ReadDirectory(descriptor))
                {
                    FuzzAssert.True(entry.Name.Length > 0 && !entry.Name.Contains('/', StringComparison.Ordinal), "An entry of a directory has the name " + FuzzAssert.Format(entry.Name));
                }

                break;

            case Operation.IsSameObject:
                FuzzAssert.True(WasiComponentFiles.IsSameObject(descriptor, descriptor), "A descriptor is not the same object as itself");
                FuzzAssert.Equal(WasiComponentFiles.IsSameObject(descriptor, other), WasiComponentFiles.IsSameObject(other, descriptor), "is-same-object is not symmetric");
                break;

            case Operation.Close:
                if (descriptor != descriptors[0])
                {
                    descriptor.Close();
                    descriptors.Remove(descriptor);
                }

                break;
        }
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        // The operations of the filesystem tests of WASI 0.3 (open, write, read, stat, rename, links, directories)
        yield return Seed("open-write-read",
            (Operation.OpenAt, 0, 0, 2 | 32 | 64, "new.txt"),
            (Operation.Write, 1, 0, 0, "hello"),
            (Operation.Read, 1, 0, 10, ""),
            (Operation.Stat, 1, 0, 0, ""),
            (Operation.SetSize, 1, 0, 1, ""),
            (Operation.Close, 1, 0, 0, ""));
        yield return Seed("directories",
            (Operation.CreateDirectoryAt, 0, 0, 0, "new"),
            (Operation.OpenAt, 0, 0, 4, "new"),
            (Operation.ReadDirectory, 0, 0, 0, ""),
            (Operation.RemoveDirectoryAt, 0, 0, 0, "."),
            (Operation.RemoveDirectoryAt, 0, 0, 0, "new"),
            (Operation.StatAt, 0, 0, 1, "dir/../file.txt"));
        yield return Seed("links",
            (Operation.SymlinkAt, 0, 0, 0, "dir/file.txt\nnew-link"),
            (Operation.ReadlinkAt, 0, 0, 0, "new-link"),
            (Operation.LinkAt, 0, 0, 0, "file.txt\nhard"),
            (Operation.OpenAt, 0, 0, 0, "file.txt"),
            (Operation.OpenAt, 0, 0, 0, "hard"),
            (Operation.IsSameObject, 1, 2, 0, ""),
            (Operation.RenameAt, 0, 0, 0, "hard\nrenamed"),
            (Operation.UnlinkFileAt, 0, 0, 0, "renamed"));
        yield return Seed("escapes",
            (Operation.OpenAt, 0, 0, 1, "../secret.txt"),
            (Operation.StatAt, 0, 0, 1, "/secret.txt"),
            (Operation.SymlinkAt, 0, 0, 0, "../secret.txt\nescape"),
            (Operation.OpenAt, 0, 0, 1, "escape"),
            (Operation.RenameAt, 0, 0, 0, "file.txt\n../moved"),
            (Operation.RemoveDirectoryAt, 0, 0, 0, ".."));
    }

    private static FuzzSeed Seed(string name, params (Operation Operation, byte Descriptor, byte Other, byte Flags, string Text)[] operations)
    {
        var bytes = new List<byte>();
        foreach (var (operation, descriptor, other, flags, text) in operations)
        {
            bytes.AddRange([(byte)operation, descriptor, other, flags]);
            bytes.AddRange(FuzzText.Encode(text));
            bytes.Add((byte)'\n');
        }

        return new FuzzSeed(name, bytes.ToArray());
    }
}
