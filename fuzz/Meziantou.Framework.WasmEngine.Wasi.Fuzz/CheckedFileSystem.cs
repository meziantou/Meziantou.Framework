using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WasmEngine.Wasi.Internal;

namespace Meziantou.Framework.WasmEngine.Wasi.Fuzz;

/// <summary>
/// A file system that checks that WASI only gives it canonical paths (relative, without <c>.</c>, <c>..</c> or empty
/// segments): its files are in the directory <c>jail</c> of an in-memory file system, next to a secret file that the
/// module must not reach.
/// </summary>
internal sealed class CheckedFileSystem : WasiFileSystem
{
    private const string Jail = "jail";

    public CheckedFileSystem()
    {
        Inner = new WasiInMemoryFileSystem { MaxFileSize = 64 * 1024 };
        Inner.AddFile("secret.txt", "secret"u8);
        Inner.AddDirectory(Jail);
    }

    public WasiInMemoryFileSystem Inner { get; }

    /// <summary>Adds a file, a directory or a link of the jail, ignoring the paths that are not valid.</summary>
    public void Add(string path, string? linkTarget, bool isDirectory)
    {
        try
        {
            if (linkTarget is not null)
            {
                Inner.AddSymbolicLink(Map(path, check: false), linkTarget);
            }
            else if (isDirectory)
            {
                Inner.AddDirectory(Map(path, check: false));
            }
            else
            {
                Inner.AddFile(Map(path, check: false), "content"u8);
            }
        }
        catch (ArgumentException)
        {
        }
    }

    private static string Map(string path, bool check = true)
    {
        if (check)
        {
            FuzzAssert.True(WasiPaths.IsCanonical(path), "WASI gave the file system a path that is not canonical: " + FuzzAssert.Format(path));
        }

        return path.Length == 0 ? Jail : Jail + "/" + path;
    }

    public override WasiFileStat? GetStat(string path) => Inner.GetStat(Map(path));

    public override string ReadSymbolicLink(string path) => Inner.ReadSymbolicLink(Map(path));

    public override Stream OpenFile(string path, FileMode mode, FileAccess access) => Inner.OpenFile(Map(path), mode, access);

    public override IEnumerable<WasiDirectoryEntry> EnumerateDirectory(string path) => Inner.EnumerateDirectory(Map(path));

    public override void CreateDirectory(string path) => Inner.CreateDirectory(Map(path));

    public override void DeleteDirectory(string path)
    {
        FuzzAssert.True(path.Length > 0, "WASI deleted the root of the file system");
        Inner.DeleteDirectory(Map(path));
    }

    public override void DeleteFile(string path) => Inner.DeleteFile(Map(path));

    public override void Move(string sourcePath, string destinationPath)
    {
        FuzzAssert.True(sourcePath.Length > 0 && destinationPath.Length > 0, "WASI moved the root of the file system");
        Inner.Move(Map(sourcePath), Map(destinationPath));
    }

    public override void SetTimes(string path, DateTimeOffset? lastAccessTime, DateTimeOffset? lastWriteTime) => Inner.SetTimes(Map(path), lastAccessTime, lastWriteTime);

    public override void CreateSymbolicLink(string path, string target) => Inner.CreateSymbolicLink(Map(path), target);

    public override void CreateHardLink(string path, string existingPath) => Inner.CreateHardLink(Map(path), Map(existingPath));

    /// <summary>Checks that the secret file outside of the jail was not changed.</summary>
    public void CheckSecret()
    {
        FuzzAssert.True(Inner.Exists("secret.txt") && Inner.ReadAllBytes("secret.txt").AsSpan().SequenceEqual("secret"u8), "The file outside of the preopened directory was changed");
        FuzzAssert.True(Inner.GetStat(Jail) is { Type: WasiFileType.Directory }, "The preopened directory is not a directory anymore");
    }
}

/// <summary>A clock whose timers expire at once: <c>poll_oneoff</c> does not wait.</summary>
internal sealed class InstantTimeProvider : TimeProvider
{
    public static InstantTimeProvider Instance { get; } = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        callback(state);
        return new CompletedTimer();
    }

    private sealed class CompletedTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
