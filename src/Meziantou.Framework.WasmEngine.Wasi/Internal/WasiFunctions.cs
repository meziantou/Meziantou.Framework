namespace Meziantou.Framework.WasmEngine.Wasi.Internal;

/// <summary>
/// The imports of the module <c>wasi_snapshot_preview1</c>: the source generator writes their host functions. The
/// functions read and write the memory of their caller (its export <c>memory</c>), and return the error code of
/// <see cref="WasiImplementation"/>, or the code of the exception it throws.
/// </summary>
internal sealed partial class WasiFunctions(WasiImplementation implementation)
{
    private const string Module = "wasi_snapshot_preview1";

    private WasmInstance? _lastInstance;
    private WasmMemory? _lastMemory;

    private delegate WasiErrno MemoryOperation(Span<byte> memory);

    private WasmMemory GetMemory(WasmCaller caller)
    {
        var instance = caller.Instance ?? throw new InvalidOperationException("The WASI functions must be called by the code of a WebAssembly instance");
        if (!ReferenceEquals(instance, _lastInstance))
        {
            _lastMemory = instance.GetMemory("memory") ?? throw new InvalidOperationException("The WebAssembly module does not export its memory as 'memory', which WASI requires");
            _lastInstance = instance;
        }

        return _lastMemory!;
    }

    private int Call(WasmCaller caller, MemoryOperation operation)
    {
        // The bytes of the memory are read at each call: they are replaced when the memory grows
        var memory = GetMemory(caller);
        try
        {
            return (int)operation(memory.GetSpan());
        }
        catch (Exception ex) when (WasiErrors.TryGetErrno(ex, out var errno))
        {
            return (int)errno;
        }
    }

    private static int Call(Func<WasiErrno> operation)
    {
        try
        {
            return (int)operation();
        }
        catch (Exception ex) when (WasiErrors.TryGetErrno(ex, out var errno))
        {
            return (int)errno;
        }
    }

    [WasmImport(Module, "args_get")]
    public int ArgsGet(WasmCaller caller, uint argv, uint argvBuffer) => Call(caller, memory => implementation.ArgsGet(memory, argv, argvBuffer));

    [WasmImport(Module, "args_sizes_get")]
    public int ArgsSizesGet(WasmCaller caller, uint count, uint bufferSize) => Call(caller, memory => implementation.ArgsSizesGet(memory, count, bufferSize));

    [WasmImport(Module, "environ_get")]
    public int EnvironGet(WasmCaller caller, uint environ, uint environBuffer) => Call(caller, memory => implementation.EnvironGet(memory, environ, environBuffer));

    [WasmImport(Module, "environ_sizes_get")]
    public int EnvironSizesGet(WasmCaller caller, uint count, uint bufferSize) => Call(caller, memory => implementation.EnvironSizesGet(memory, count, bufferSize));

    [WasmImport(Module, "clock_res_get")]
    public int ClockResGet(WasmCaller caller, uint id, uint resolution) => Call(caller, memory => implementation.ClockResGet(memory, id, resolution));

    [WasmImport(Module, "clock_time_get")]
    public int ClockTimeGet(WasmCaller caller, uint id, ulong precision, uint time) => Call(caller, memory => implementation.ClockTimeGet(memory, id, precision, time));

    [WasmImport(Module, "fd_advise")]
    public int FdAdvise(int fd, ulong offset, ulong length, uint advice) => Call(() => implementation.FdAdvise(fd, offset, length, (byte)advice));

    [WasmImport(Module, "fd_allocate")]
    public int FdAllocate(int fd, ulong offset, ulong length) => Call(() => implementation.FdAllocate(fd, offset, length));

    [WasmImport(Module, "fd_close")]
    public int FdClose(int fd) => Call(() => implementation.FdClose(fd));

    [WasmImport(Module, "fd_datasync")]
    public int FdDatasync(int fd) => Call(() => implementation.FdDatasync(fd));

    [WasmImport(Module, "fd_fdstat_get")]
    public int FdFdstatGet(WasmCaller caller, int fd, uint stat) => Call(caller, memory => implementation.FdFdstatGet(memory, fd, stat));

    [WasmImport(Module, "fd_fdstat_set_flags")]
    public int FdFdstatSetFlags(int fd, uint flags) => Call(() => implementation.FdFdstatSetFlags(fd, (ushort)flags));

    [WasmImport(Module, "fd_fdstat_set_rights")]
    public int FdFdstatSetRights(int fd, ulong rightsBase, ulong rightsInheriting) => Call(() => implementation.FdFdstatSetRights(fd, rightsBase, rightsInheriting));

    [WasmImport(Module, "fd_filestat_get")]
    public int FdFilestatGet(WasmCaller caller, int fd, uint stat) => Call(caller, memory => implementation.FdFilestatGet(memory, fd, stat));

    [WasmImport(Module, "fd_filestat_set_size")]
    public int FdFilestatSetSize(int fd, ulong size) => Call(() => implementation.FdFilestatSetSize(fd, size));

    [WasmImport(Module, "fd_filestat_set_times")]
    public int FdFilestatSetTimes(int fd, ulong accessTime, ulong writeTime, uint flags) => Call(() => implementation.FdFilestatSetTimes(fd, accessTime, writeTime, (ushort)flags));

    [WasmImport(Module, "fd_pread")]
    public int FdPread(WasmCaller caller, int fd, uint iovecs, uint iovecCount, ulong offset, uint read) => Call(caller, memory => implementation.FdPread(memory, fd, iovecs, iovecCount, offset, read));

    [WasmImport(Module, "fd_prestat_get")]
    public int FdPrestatGet(WasmCaller caller, int fd, uint prestat) => Call(caller, memory => implementation.FdPrestatGet(memory, fd, prestat));

    [WasmImport(Module, "fd_prestat_dir_name")]
    public int FdPrestatDirName(WasmCaller caller, int fd, uint path, uint length) => Call(caller, memory => implementation.FdPrestatDirName(memory, fd, path, length));

    [WasmImport(Module, "fd_pwrite")]
    public int FdPwrite(WasmCaller caller, int fd, uint iovecs, uint iovecCount, ulong offset, uint written) => Call(caller, memory => implementation.FdPwrite(memory, fd, iovecs, iovecCount, offset, written));

    [WasmImport(Module, "fd_read")]
    public int FdRead(WasmCaller caller, int fd, uint iovecs, uint iovecCount, uint read) => Call(caller, memory => implementation.FdRead(memory, fd, iovecs, iovecCount, read));

    [WasmImport(Module, "fd_readdir")]
    public int FdReaddir(WasmCaller caller, int fd, uint buffer, uint bufferLength, ulong cookie, uint used) => Call(caller, memory => implementation.FdReaddir(memory, fd, buffer, bufferLength, cookie, used));

    [WasmImport(Module, "fd_renumber")]
    public int FdRenumber(int fd, int to) => Call(() => implementation.FdRenumber(fd, to));

    [WasmImport(Module, "fd_seek")]
    public int FdSeek(WasmCaller caller, int fd, long offset, uint whence, uint position) => Call(caller, memory => implementation.FdSeek(memory, fd, offset, (byte)whence, position));

    [WasmImport(Module, "fd_sync")]
    public int FdSync(int fd) => Call(() => implementation.FdSync(fd));

    [WasmImport(Module, "fd_tell")]
    public int FdTell(WasmCaller caller, int fd, uint position) => Call(caller, memory => implementation.FdTell(memory, fd, position));

    [WasmImport(Module, "fd_write")]
    public int FdWrite(WasmCaller caller, int fd, uint iovecs, uint iovecCount, uint written) => Call(caller, memory => implementation.FdWrite(memory, fd, iovecs, iovecCount, written));

    [WasmImport(Module, "path_create_directory")]
    public int PathCreateDirectory(WasmCaller caller, int fd, uint path, uint pathLength) => Call(caller, memory => implementation.PathCreateDirectory(memory, fd, path, pathLength));

    [WasmImport(Module, "path_filestat_get")]
    public int PathFilestatGet(WasmCaller caller, int fd, uint flags, uint path, uint pathLength, uint stat) => Call(caller, memory => implementation.PathFilestatGet(memory, fd, flags, path, pathLength, stat));

    [WasmImport(Module, "path_filestat_set_times")]
    public int PathFilestatSetTimes(WasmCaller caller, int fd, uint flags, uint path, uint pathLength, ulong accessTime, ulong writeTime, uint timeFlags) => Call(caller, memory => implementation.PathFilestatSetTimes(memory, fd, flags, path, pathLength, accessTime, writeTime, (ushort)timeFlags));

    [WasmImport(Module, "path_link")]
    public int PathLink(WasmCaller caller, int oldFd, uint oldFlags, uint oldPath, uint oldPathLength, int newFd, uint newPath, uint newPathLength) => Call(caller, memory => implementation.PathLink(memory, oldFd, oldFlags, oldPath, oldPathLength, newFd, newPath, newPathLength));

    [WasmImport(Module, "path_open")]
    public int PathOpen(WasmCaller caller, int fd, uint lookupFlags, uint path, uint pathLength, uint openFlags, ulong rightsBase, ulong rightsInheriting, uint fdFlags, uint opened) => Call(caller, memory => implementation.PathOpen(memory, fd, lookupFlags, path, pathLength, (ushort)openFlags, rightsBase, rightsInheriting, (ushort)fdFlags, opened));

    [WasmImport(Module, "path_readlink")]
    public int PathReadlink(WasmCaller caller, int fd, uint path, uint pathLength, uint buffer, uint bufferLength, uint used) => Call(caller, memory => implementation.PathReadlink(memory, fd, path, pathLength, buffer, bufferLength, used));

    [WasmImport(Module, "path_remove_directory")]
    public int PathRemoveDirectory(WasmCaller caller, int fd, uint path, uint pathLength) => Call(caller, memory => implementation.PathRemoveDirectory(memory, fd, path, pathLength));

    [WasmImport(Module, "path_rename")]
    public int PathRename(WasmCaller caller, int fd, uint oldPath, uint oldPathLength, int newFd, uint newPath, uint newPathLength) => Call(caller, memory => implementation.PathRename(memory, fd, oldPath, oldPathLength, newFd, newPath, newPathLength));

    [WasmImport(Module, "path_symlink")]
    public int PathSymlink(WasmCaller caller, uint target, uint targetLength, int fd, uint path, uint pathLength) => Call(caller, memory => implementation.PathSymlink(memory, target, targetLength, fd, path, pathLength));

    [WasmImport(Module, "path_unlink_file")]
    public int PathUnlinkFile(WasmCaller caller, int fd, uint path, uint pathLength) => Call(caller, memory => implementation.PathUnlinkFile(memory, fd, path, pathLength));

    [WasmImport(Module, "poll_oneoff")]
    public int PollOneoff(WasmCaller caller, uint subscriptions, uint events, uint count, uint eventCount) => Call(caller, memory => implementation.PollOneoff(memory, subscriptions, events, count, eventCount));

    [WasmImport(Module, "proc_exit")]
    public void ProcExit(uint code) => implementation.ProcExit(code);

    [WasmImport(Module, "proc_raise")]
    public int ProcRaise(uint signal) => (int)implementation.ProcRaise(signal);

    [WasmImport(Module, "random_get")]
    public int RandomGet(WasmCaller caller, uint buffer, uint length) => Call(caller, memory => implementation.RandomGet(memory, buffer, length));

    [WasmImport(Module, "sched_yield")]
    public int SchedYield() => (int)implementation.SchedYield();

    [WasmImport(Module, "sock_accept")]
    public int SockAccept(int fd, uint flags, uint accepted)
    {
        _ = flags;
        _ = accepted;
        return Call(() => implementation.SockAccept(fd));
    }

    [WasmImport(Module, "sock_recv")]
    public int SockRecv(int fd, uint data, uint dataLength, uint flags, uint received, uint receivedFlags)
    {
        _ = (data, dataLength, flags, received, receivedFlags);
        return Call(() => implementation.SockRecv(fd));
    }

    [WasmImport(Module, "sock_send")]
    public int SockSend(int fd, uint data, uint dataLength, uint flags, uint sent)
    {
        _ = (data, dataLength, flags, sent);
        return Call(() => implementation.SockSend(fd));
    }

    [WasmImport(Module, "sock_shutdown")]
    public int SockShutdown(int fd, uint how)
    {
        _ = how;
        return Call(() => implementation.SockShutdown(fd));
    }
}
