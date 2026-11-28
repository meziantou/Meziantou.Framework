using Meziantou.Framework.WasmEngine.Components;
using static Meziantou.Framework.WasmEngine.Wasi.Components.WasiComponentValues;

namespace Meziantou.Framework.WasmEngine.Wasi.Components;

/// <summary>The interfaces of WASI 0.2 (wasi:cli, wasi:io, wasi:clocks, wasi:random, wasi:filesystem) for components.</summary>
internal sealed class WasiPreview2
{
    public const string Version = "0.2.12";

    private readonly WasiComponentState _state;
    private readonly ComponentResourceType _error = new("error");
    private readonly ComponentResourceType _pollable = new("pollable");
    private readonly ComponentResourceType _inputStream;
    private readonly ComponentResourceType _outputStream;
    private readonly ComponentResourceType _terminalInput = new("terminal-input");
    private readonly ComponentResourceType _terminalOutput = new("terminal-output");
    private readonly ComponentResourceType _descriptor;
    private readonly ComponentResourceType _directoryEntryStream = new("directory-entry-stream");

    public WasiPreview2(WasiComponentState state)
    {
        _state = state;
        _inputStream = new("input-stream", resource => ((InputStream)resource.Representation!).Dispose());
        _outputStream = new("output-stream", resource => ((OutputStream)resource.Representation!).Dispose());
        _descriptor = new("descriptor", resource => ((WasiFileDescriptor)resource.Representation!).Close());
    }

    private static string Name(string interfaceName) => interfaceName + "@" + Version;

    public void Define(ComponentImports imports)
    {
        DefineIo(imports);
        DefineCli(imports);
        DefineClocks(imports);
        DefineRandom(imports);
        DefineFilesystem(imports);
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:io
    // ------------------------------------------------------------------------------------------------------------

    private abstract class Pollable
    {
        public abstract bool Ready();

        public abstract void Block();
    }

    private sealed class ReadyPollable : Pollable
    {
        public static ReadyPollable Instance { get; } = new();

        public override bool Ready() => true;

        public override void Block()
        {
        }
    }

    private sealed class DeadlinePollable(WasiComponentState state, ulong deadline) : Pollable
    {
        public ulong Deadline => deadline;

        public override bool Ready() => state.MonotonicNow() >= deadline;

        public override void Block() => state.SleepUntil(deadline);
    }

    /// <summary>An input stream: a .NET stream, read until its end (a null stream is empty).</summary>
    private sealed class InputStream(Stream? stream, bool ownsStream) : IDisposable
    {
        public bool Closed { get; private set; }

        public byte[] Read(ulong length, bool blocking)
        {
            _ = blocking;
            if (Closed || stream is null)
            {
                Closed = true;
                throw new StreamClosedException();
            }

            var buffer = new byte[(int)Math.Min(length, 64 * 1024)];
            if (buffer.Length == 0)
                return [];

            var read = stream.Read(buffer);
            if (read == 0)
            {
                Closed = true;
                throw new StreamClosedException();
            }

            return read == buffer.Length ? buffer : buffer[..read];
        }

        public void Dispose()
        {
            if (ownsStream)
            {
                stream?.Dispose();
            }
        }
    }

    /// <summary>An output stream: a .NET stream (a null stream discards the bytes).</summary>
    private sealed class OutputStream(Stream? stream, bool ownsStream) : IDisposable
    {
        public const ulong WriteBudget = 1024 * 1024;

        public void Write(ReadOnlySpan<byte> bytes) => stream?.Write(bytes);

        public void Flush() => stream?.Flush();

        public void Dispose()
        {
            if (ownsStream)
            {
                stream?.Dispose();
            }
            else
            {
                stream?.Flush();
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "The exception is converted to a stream error: it never reaches the host")]
    private sealed class StreamClosedException : Exception
    {
        public StreamClosedException()
            : base("The stream is closed")
        {
        }

        public StreamClosedException(string message)
            : base(message)
        {
        }

        public StreamClosedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private sealed class DirectoryEntryStream(IReadOnlyList<WasiDirectoryEntry> entries)
    {
        private int _index;

        public WasiDirectoryEntry? Next() => _index < entries.Count ? entries[_index++] : null;
    }

    /// <summary>Converts the exceptions of a stream operation to a <c>stream-error</c>.</summary>
    private ComponentValue StreamResult(Func<ComponentValue?> operation)
    {
        try
        {
            return ComponentValue.Ok(operation());
        }
        catch (StreamClosedException)
        {
            return ComponentValue.Error(ComponentValue.FromCase("closed"));
        }
        catch (Exception ex) when (ex is IOException or WasiException or NotSupportedException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return ComponentValue.Error(ComponentValue.FromCase("last-operation-failed", Resource(_error, ex)));
        }
    }

    private void DefineIo(ComponentImports imports)
    {
        imports.Define(Name("wasi:io/error"), new ComponentImports()
            .Define("error", _error)
            .Define("[method]error.to-debug-string", Function((context, arguments) => String(Get<Exception>(arguments[0]).Message))));

        imports.Define(Name("wasi:io/poll"), new ComponentImports()
            .Define("pollable", _pollable)
            .Define("[method]pollable.ready", Function((context, arguments) => ComponentValue.FromBool(Get<Pollable>(arguments[0]).Ready())))
            .Define("[method]pollable.block", Function((context, arguments) =>
            {
                Get<Pollable>(arguments[0]).Block();
                return null;
            }))
            .Define("poll", Function((context, arguments) => Poll(arguments[0]))));

        imports.Define(Name("wasi:io/streams"), new ComponentImports()
            .Define("input-stream", _inputStream)
            .Define("output-stream", _outputStream)
            .Define("[method]input-stream.read", Function((context, arguments) => ReadStream(arguments, blocking: false)))
            .Define("[method]input-stream.blocking-read", Function((context, arguments) => ReadStream(arguments, blocking: true)))
            .Define("[method]input-stream.skip", Function((context, arguments) => SkipStream(arguments, blocking: false)))
            .Define("[method]input-stream.blocking-skip", Function((context, arguments) => SkipStream(arguments, blocking: true)))
            .Define("[method]input-stream.subscribe", Function((context, arguments) => Resource(_pollable, ReadyPollable.Instance)))
            .Define("[method]output-stream.check-write", Function((context, arguments) => StreamResult(() => ComponentValue.FromU64(OutputStream.WriteBudget))))
            .Define("[method]output-stream.write", Function((context, arguments) => WriteStream(arguments[0], arguments[1].AsBytes(), flush: false)))
            .Define("[method]output-stream.blocking-write-and-flush", Function((context, arguments) => WriteStream(arguments[0], arguments[1].AsBytes(), flush: true)))
            .Define("[method]output-stream.flush", Function((context, arguments) => FlushStream(arguments[0])))
            .Define("[method]output-stream.blocking-flush", Function((context, arguments) => FlushStream(arguments[0])))
            .Define("[method]output-stream.subscribe", Function((context, arguments) => Resource(_pollable, ReadyPollable.Instance)))
            .Define("[method]output-stream.write-zeroes", Function((context, arguments) => WriteStream(arguments[0], new byte[(int)Math.Min(arguments[1].AsU64(), OutputStream.WriteBudget)], flush: false)))
            .Define("[method]output-stream.blocking-write-zeroes-and-flush", Function((context, arguments) => WriteStream(arguments[0], new byte[(int)Math.Min(arguments[1].AsU64(), OutputStream.WriteBudget)], flush: true)))
            .Define("[method]output-stream.splice", Function((context, arguments) => Splice(arguments)))
            .Define("[method]output-stream.blocking-splice", Function((context, arguments) => Splice(arguments))));
    }

    private static ComponentValue Poll(ComponentValue pollables)
    {
        var items = pollables.AsList().Select(Get<Pollable>).ToArray();
        if (items.Length == 0)
            throw new WasmTrapException("poll requires at least one pollable");

        while (true)
        {
            var ready = new List<ComponentValue>();
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i].Ready())
                {
                    ready.Add(ComponentValue.FromU32((uint)i));
                }
            }

            if (ready.Count > 0)
                return ComponentValue.FromList(ready);

            // Every pollable that is not ready is a deadline: wait for the earliest one
            var earliest = items.OfType<DeadlinePollable>().MinBy(pollable => pollable.Deadline);
            if (earliest is null)
            {
                items[0].Block();
                continue;
            }

            earliest.Block();
        }
    }

    private ComponentValue ReadStream(ReadOnlySpan<ComponentValue> arguments, bool blocking)
    {
        var stream = Get<InputStream>(arguments[0]);
        var length = arguments[1].AsU64();
        return StreamResult(() => ComponentValue.FromBytes(stream.Read(length, blocking)));
    }

    private ComponentValue SkipStream(ReadOnlySpan<ComponentValue> arguments, bool blocking)
    {
        var stream = Get<InputStream>(arguments[0]);
        var length = arguments[1].AsU64();
        return StreamResult(() => ComponentValue.FromU64((ulong)stream.Read(length, blocking).Length));
    }

    private ComponentValue WriteStream(ComponentValue resource, byte[] bytes, bool flush)
    {
        var stream = Get<OutputStream>(resource);
        return StreamResult(() =>
        {
            stream.Write(bytes);
            if (flush)
            {
                stream.Flush();
            }

            return null;
        });
    }

    private ComponentValue FlushStream(ComponentValue resource)
    {
        var stream = Get<OutputStream>(resource);
        return StreamResult(() =>
        {
            stream.Flush();
            return null;
        });
    }

    private ComponentValue Splice(ReadOnlySpan<ComponentValue> arguments)
    {
        var output = Get<OutputStream>(arguments[0]);
        var input = Get<InputStream>(arguments[1]);
        var length = arguments[2].AsU64();
        return StreamResult(() =>
        {
            var bytes = input.Read(length, blocking: true);
            output.Write(bytes);
            return ComponentValue.FromU64((ulong)bytes.Length);
        });
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:cli
    // ------------------------------------------------------------------------------------------------------------

    private void DefineCli(ComponentImports imports)
    {
        imports.Define(Name("wasi:cli/environment"), new ComponentImports()
            .Define("get-environment", Function((context, arguments) => Environment(_state.EnvironmentVariables)))
            .Define("get-arguments", Function((context, arguments) => Strings(_state.Arguments)))
            .Define("initial-cwd", Function((context, arguments) => ComponentValue.NoneValue)));

        imports.Define(Name("wasi:cli/exit"), new ComponentImports()
            .Define("exit", Function((context, arguments) => throw new WasiExitException(arguments[0].IsCase("ok") ? 0 : 1)))
            .Define("exit-with-code", Function((context, arguments) => throw new WasiExitException(arguments[0].AsU8()))));

        imports.Define(Name("wasi:cli/stdin"), new ComponentImports()
            .Define("get-stdin", Function((context, arguments) => Resource(_inputStream, new InputStream(_state.StandardInput, ownsStream: false)))));
        imports.Define(Name("wasi:cli/stdout"), new ComponentImports()
            .Define("get-stdout", Function((context, arguments) => Resource(_outputStream, new OutputStream(_state.StandardOutput, ownsStream: false)))));
        imports.Define(Name("wasi:cli/stderr"), new ComponentImports()
            .Define("get-stderr", Function((context, arguments) => Resource(_outputStream, new OutputStream(_state.StandardError, ownsStream: false)))));

        imports.Define(Name("wasi:cli/terminal-input"), new ComponentImports().Define("terminal-input", _terminalInput));
        imports.Define(Name("wasi:cli/terminal-output"), new ComponentImports().Define("terminal-output", _terminalOutput));
        imports.Define(Name("wasi:cli/terminal-stdin"), new ComponentImports().Define("get-terminal-stdin", Function((context, arguments) => ComponentValue.NoneValue)));
        imports.Define(Name("wasi:cli/terminal-stdout"), new ComponentImports().Define("get-terminal-stdout", Function((context, arguments) => ComponentValue.NoneValue)));
        imports.Define(Name("wasi:cli/terminal-stderr"), new ComponentImports().Define("get-terminal-stderr", Function((context, arguments) => ComponentValue.NoneValue)));
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:clocks and wasi:random
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentValue DateTime(long seconds, uint nanoseconds) => ComponentValue.FromRecord(ComponentValue.FromU64((ulong)Math.Max(0, seconds)), ComponentValue.FromU32(nanoseconds));

    private void DefineClocks(ComponentImports imports)
    {
        imports.Define(Name("wasi:clocks/monotonic-clock"), new ComponentImports()
            .Define("now", Function((context, arguments) => ComponentValue.FromU64(_state.MonotonicNow())))
            .Define("resolution", Function((context, arguments) => ComponentValue.FromU64(_state.MonotonicResolution)))
            .Define("subscribe-instant", Function((context, arguments) => Resource(_pollable, new DeadlinePollable(_state, arguments[0].AsU64()))))
            .Define("subscribe-duration", Function((context, arguments) =>
            {
                var now = _state.MonotonicNow();
                var duration = arguments[0].AsU64();
                return Resource(_pollable, new DeadlinePollable(_state, duration > ulong.MaxValue - now ? ulong.MaxValue : now + duration));
            })));

        imports.Define(Name("wasi:clocks/wall-clock"), new ComponentImports()
            .Define("now", Function((context, arguments) =>
            {
                var (seconds, nanoseconds) = _state.SystemNow();
                return DateTime(seconds, nanoseconds);
            }))
            .Define("resolution", Function((context, arguments) => DateTime(0, 100))));
    }

    private void DefineRandom(ComponentImports imports)
    {
        imports.Define(Name("wasi:random/random"), new ComponentImports()
            .Define("get-random-bytes", Function((context, arguments) => ComponentValue.FromBytes(_state.GetRandomBytes(arguments[0].AsU64()))))
            .Define("get-random-u64", Function((context, arguments) => ComponentValue.FromU64(_state.GetRandomU64()))));

        imports.Define(Name("wasi:random/insecure"), new ComponentImports()
            .Define("get-insecure-random-bytes", Function((context, arguments) => ComponentValue.FromBytes(_state.GetRandomBytes(arguments[0].AsU64()))))
            .Define("get-insecure-random-u64", Function((context, arguments) => ComponentValue.FromU64(_state.GetRandomU64()))));

        imports.Define(Name("wasi:random/insecure-seed"), new ComponentImports()
            .Define("insecure-seed", Function((context, arguments) => ComponentValue.FromTuple(ComponentValue.FromU64(_state.InsecureSeed.Item1), ComponentValue.FromU64(_state.InsecureSeed.Item2)))));
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:filesystem
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentValue Result(Func<ComponentValue?> operation)
    {
        try
        {
            return ComponentValue.Ok(operation());
        }
        catch (Exception ex) when (WasiComponentFiles.IsError(ex))
        {
            return ComponentValue.Error(ComponentValue.FromEnum(WasiComponentFiles.GetErrorCode(WasiComponentFiles.GetErrno(ex))));
        }
    }

    private static ComponentValue Unit(Action operation) => Result(() =>
    {
        operation();
        return null;
    });

    private static string GetType(WasiFileType type) => type switch
    {
        WasiFileType.BlockDevice => "block-device",
        WasiFileType.CharacterDevice => "character-device",
        WasiFileType.Directory => "directory",
        WasiFileType.RegularFile => "regular-file",
        WasiFileType.SymbolicLink => "symbolic-link",
        WasiFileType.SocketDatagram or WasiFileType.SocketStream => "socket",
        _ => "unknown",
    };

    private static ComponentValue Timestamp(DateTimeOffset value)
    {
        var ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        return ComponentValue.Some(DateTime(ticks / TimeSpan.TicksPerSecond, (uint)(ticks % TimeSpan.TicksPerSecond * 100)));
    }

    private static ComponentValue Stat(WasiFileStat stat) => ComponentValue.FromRecord(
        ComponentValue.FromEnum(GetType(stat.Type)),
        ComponentValue.FromU64(stat.LinkCount),
        ComponentValue.FromU64(stat.Size),
        Timestamp(stat.LastAccessTime),
        Timestamp(stat.LastWriteTime),
        Timestamp(stat.ChangeTime));

    private DateTimeOffset? NewTimestamp(ComponentValue value)
    {
        if (value.IsCase("no-change"))
            return null;

        if (value.IsCase("now"))
            return _state.TimeProvider.GetUtcNow();

        var datetime = value.Payload!.Value.AsRecord();
        // AddSeconds throws for a time out of the range of DateTimeOffset: the operation fails with invalid-argument
        return DateTimeOffset.UnixEpoch.AddSeconds(datetime[0].AsU64()).AddTicks(datetime[1].AsU32() / 100);
    }

    private static ComponentValue MetadataHash(WasiFileSystem fileSystem, WasiFileStat stat)
    {
        var (lower, upper) = WasiComponentFiles.MetadataHash(fileSystem, stat);
        return ComponentValue.FromRecord(ComponentValue.FromU64(lower), ComponentValue.FromU64(upper));
    }

    private void DefineFilesystem(ComponentImports imports)
    {
        WasiFileDescriptor D(ComponentValue value) => Get<WasiFileDescriptor>(value);

        var types = new ComponentImports()
            .Define("descriptor", _descriptor)
            .Define("directory-entry-stream", _directoryEntryStream)
            .Define("[method]descriptor.read-via-stream", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                // The offsets past the end of the largest file are the end of the file
                var offset = (long)Math.Min(arguments[1].AsU64(), long.MaxValue);
                return Result(() =>
                {
                    var stream = descriptor.GetStream();
                    if (!descriptor.CanRead)
                        throw new WasiException(WasiErrno.BadFileDescriptor);

                    return Resource(_inputStream, new InputStream(new WasiFileRangeStream(stream, offset, append: false), ownsStream: false));
                });
            }))
            .Define("[method]descriptor.write-via-stream", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                // The offsets past the end of the largest file are the end of the file
                var offset = (long)Math.Min(arguments[1].AsU64(), long.MaxValue);
                return Result(() => OpenOutput(descriptor, offset, append: false));
            }))
            .Define("[method]descriptor.append-via-stream", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                return Result(() => OpenOutput(descriptor, 0, append: true));
            }))
            .Define("[method]descriptor.advise", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                return Unit(() => descriptor.GetStream());
            }))
            .Define("[method]descriptor.sync-data", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                return Unit(() => WasiComponentFiles.Sync(descriptor));
            }))
            .Define("[method]descriptor.sync", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                return Unit(() => WasiComponentFiles.Sync(descriptor));
            }))
            .Define("[method]descriptor.get-flags", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var flags = new List<string>();
                if (descriptor.CanRead)
                {
                    flags.Add("read");
                }

                if (descriptor.CanWrite)
                {
                    flags.Add("write");
                }

                if (descriptor.CanMutate)
                {
                    flags.Add("mutate-directory");
                }

                return ComponentValue.Ok(ComponentValue.FromFlags(flags));
            }))
            .Define("[method]descriptor.get-type", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                return Result(() => ComponentValue.FromEnum(GetType(WasiComponentFiles.Stat(descriptor).Type)));
            }))
            .Define("[method]descriptor.set-size", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var size = arguments[1].AsU64();
                return Unit(() => WasiComponentFiles.SetSize(descriptor, size));
            }))
            .Define("[method]descriptor.set-times", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var access = arguments[1];
                var write = arguments[2];
                return Unit(() => WasiComponentFiles.SetTimes(descriptor, NewTimestamp(access), NewTimestamp(write)));
            }))
            .Define("[method]descriptor.read", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var length = arguments[1].AsU64();
                var offset = arguments[2].AsU64();
                return Result(() =>
                {
                    var (bytes, endOfFile) = WasiComponentFiles.Read(descriptor, length, offset);
                    return ComponentValue.FromTuple(ComponentValue.FromBytes(bytes), ComponentValue.FromBool(endOfFile));
                });
            }))
            .Define("[method]descriptor.write", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var bytes = arguments[1].AsBytes();
                var offset = arguments[2].AsU64();
                return Result(() => ComponentValue.FromU64(WasiComponentFiles.Write(descriptor, bytes, offset)));
            }))
            .Define("[method]descriptor.read-directory", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                return Result(() => Resource(_directoryEntryStream, new DirectoryEntryStream(WasiComponentFiles.ReadDirectory(descriptor))));
            }))
            .Define("[method]descriptor.create-directory-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var path = arguments[1].AsString();
                return Unit(() => WasiComponentFiles.CreateDirectoryAt(descriptor, path));
            }))
            .Define("[method]descriptor.stat", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                return Result(() => Stat(WasiComponentFiles.Stat(descriptor)));
            }))
            .Define("[method]descriptor.stat-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var follow = HasFlag(arguments[1], "symlink-follow");
                var path = arguments[2].AsString();
                return Result(() => Stat(WasiComponentFiles.StatAt(descriptor, follow, path)));
            }))
            .Define("[method]descriptor.set-times-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var follow = HasFlag(arguments[1], "symlink-follow");
                var path = arguments[2].AsString();
                var access = arguments[3];
                var write = arguments[4];
                return Unit(() => WasiComponentFiles.SetTimesAt(descriptor, follow, path, NewTimestamp(access), NewTimestamp(write)));
            }))
            .Define("[method]descriptor.link-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var follow = HasFlag(arguments[1], "symlink-follow");
                var oldPath = arguments[2].AsString();
                var newDescriptor = D(arguments[3]);
                var newPath = arguments[4].AsString();
                return Unit(() => WasiComponentFiles.LinkAt(descriptor, follow, oldPath, newDescriptor, newPath));
            }))
            .Define("[method]descriptor.open-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var follow = HasFlag(arguments[1], "symlink-follow");
                var path = arguments[2].AsString();
                var openFlags = arguments[3];
                var flags = arguments[4];
                return Result(() => Resource(_descriptor, WasiComponentFiles.OpenAt(
                    descriptor,
                    follow,
                    path,
                    create: HasFlag(openFlags, "create"),
                    mustBeDirectory: HasFlag(openFlags, "directory"),
                    exclusive: HasFlag(openFlags, "exclusive"),
                    truncate: HasFlag(openFlags, "truncate"),
                    read: HasFlag(flags, "read"),
                    write: HasFlag(flags, "write"),
                    mutate: HasFlag(flags, "mutate-directory"))));
            }))
            .Define("[method]descriptor.readlink-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var path = arguments[1].AsString();
                return Result(() => String(WasiComponentFiles.ReadlinkAt(descriptor, path)));
            }))
            .Define("[method]descriptor.remove-directory-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var path = arguments[1].AsString();
                return Unit(() => WasiComponentFiles.RemoveDirectoryAt(descriptor, path));
            }))
            .Define("[method]descriptor.rename-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var oldPath = arguments[1].AsString();
                var newDescriptor = D(arguments[2]);
                var newPath = arguments[3].AsString();
                return Unit(() => WasiComponentFiles.RenameAt(descriptor, oldPath, newDescriptor, newPath));
            }))
            .Define("[method]descriptor.symlink-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var target = arguments[1].AsString();
                var path = arguments[2].AsString();
                return Unit(() => WasiComponentFiles.SymlinkAt(descriptor, target, path));
            }))
            .Define("[method]descriptor.unlink-file-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var path = arguments[1].AsString();
                return Unit(() => WasiComponentFiles.UnlinkFileAt(descriptor, path));
            }))
            .Define("[method]descriptor.is-same-object", Function((context, arguments) => ComponentValue.FromBool(WasiComponentFiles.IsSameObject(D(arguments[0]), D(arguments[1])))))
            .Define("[method]descriptor.metadata-hash", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                return Result(() => MetadataHash(descriptor.FileSystem, WasiComponentFiles.Stat(descriptor)));
            }))
            .Define("[method]descriptor.metadata-hash-at", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var follow = HasFlag(arguments[1], "symlink-follow");
                var path = arguments[2].AsString();
                return Result(() => MetadataHash(descriptor.FileSystem, WasiComponentFiles.StatAt(descriptor, follow, path)));
            }))
            .Define("[method]directory-entry-stream.read-directory-entry", Function((context, arguments) =>
            {
                var stream = Get<DirectoryEntryStream>(arguments[0]);
                return Result(() => stream.Next() is { } entry
                    ? ComponentValue.Some(ComponentValue.FromRecord(ComponentValue.FromEnum(GetType(entry.Type)), String(entry.Name)))
                    : ComponentValue.NoneValue);
            }))
            .Define("filesystem-error-code", Function((context, arguments) =>
            {
                var error = Get<Exception>(arguments[0]);
                return WasiComponentFiles.IsError(error) ? ComponentValue.Some(ComponentValue.FromEnum(WasiComponentFiles.GetErrorCode(WasiComponentFiles.GetErrno(error)))) : ComponentValue.NoneValue;
            }));

        imports.Define(Name("wasi:filesystem/types"), types);
        imports.Define(Name("wasi:filesystem/preopens"), new ComponentImports()
            .Define("get-directories", Function((context, arguments) => ComponentValue.FromList(_state.Preopens.Select(preopen => ComponentValue.FromTuple(
                Resource(_descriptor, new WasiFileDescriptor(preopen.FileSystem, "", isDirectory: true, stream: null, canRead: true, canWrite: false, canMutate: !preopen.FileSystem.IsReadOnly)),
                String(preopen.GuestPath)))))));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The resource owns the stream: its destructor disposes it")]
    private ComponentValue OpenOutput(WasiFileDescriptor descriptor, long offset, bool append)
    {
        var stream = descriptor.GetStream();
        if (!descriptor.CanWrite)
            throw new WasiException(WasiErrno.BadFileDescriptor);

        return Resource(_outputStream, new OutputStream(new WasiFileRangeStream(stream, offset, append), ownsStream: false));
    }
}
