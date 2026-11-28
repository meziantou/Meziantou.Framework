using Meziantou.Framework.WasmEngine.Components;
using static Meziantou.Framework.WasmEngine.Wasi.Components.WasiComponentValues;

namespace Meziantou.Framework.WasmEngine.Wasi.Components;

/// <summary>
/// The interfaces of WASI 0.3 (wasi:cli, wasi:clocks, wasi:random, wasi:filesystem) for components: the streams of bytes
/// are <c>stream&lt;u8&gt;</c> values, copied from and to the .NET streams in the background.
/// </summary>
internal sealed class WasiPreview3
{
    public const string Version = "0.3.2";

    private const int BufferSize = 64 * 1024;

    private readonly WasiComponentState _state;
    private readonly ComponentResourceType _terminalInput = new("terminal-input");
    private readonly ComponentResourceType _terminalOutput = new("terminal-output");
    private readonly ComponentResourceType _descriptor;

    public WasiPreview3(WasiComponentState state)
    {
        _state = state;
        _descriptor = new("descriptor", resource => ((WasiFileDescriptor)resource.Representation!).Close());
        Http = new WasiHttp(state.HttpClient);
    }

    /// <summary>The interfaces of wasi:http, whose resource types the calls of the handlers of the components use.</summary>
    public WasiHttp Http { get; }

    private static string Name(string interfaceName) => interfaceName + "@" + Version;

    public void Define(ComponentImports imports)
    {
        DefineCli(imports);
        DefineClocks(imports);
        DefineRandom(imports);
        DefineFilesystem(imports);
        new WasiSockets(_state.Network).Define(imports, Name);
        Http.Define(imports, Name);
    }

    /// <summary>Creates an asynchronous function of the host whose result is computed synchronously.</summary>
    private static ComponentFunction Async(Func<ComponentValue[], ComponentValue?> function) => AsyncFunction((context, arguments) => ValueTask.FromResult(function(arguments)));

    private static (ComponentValueType? StreamElement, ComponentValueType? FutureElement) GetStreamAndFutureTypes(ComponentFunctionType type)
    {
        if (type.Result is ComponentTupleType { Types: [ComponentStreamType stream, ComponentFutureType future] })
            return (stream.ElementType, future.ElementType);

        throw new WasmTypeMismatchException("The function must return a tuple of a stream and a future, not " + type.Result);
    }

    private static ComponentValueType? GetFutureType(ComponentFunctionType type) => type.Result is ComponentFutureType future
        ? future.ElementType
        : throw new WasmTypeMismatchException("The function must return a future, not " + type.Result);

    // ------------------------------------------------------------------------------------------------------------
    // Streams
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Copies a .NET stream to a new <c>stream&lt;u8&gt;</c>: returns the readable end and the future of the result of the copy.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The background copy disposes the writer")]
    private static ComponentValue ReadViaStream(ComponentCallContext context, Func<Stream?> open, Func<Exception, ComponentValue> getError)
    {
        var (streamType, futureType) = GetStreamAndFutureTypes(context.Type);
        var writer = ComponentStreamWriter.Create(streamType, out var readable);
        var result = ComponentFutureWriter.Create(futureType, out var future);
        context.RunInBackground(async () =>
        {
            ComponentValue status;
            try
            {
                var stream = open();
                if (stream is not null)
                {
                    // The copy ends when the component drops the stream: the data that follows stays in the .NET stream
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, writer.ReaderDroppedToken);
                    var buffer = new byte[BufferSize];
                    while (!writer.IsReaderDropped)
                    {
                        var read = await stream.ReadAsync(buffer, cancellation.Token).ConfigureAwait(true);
                        if (read == 0)
                            break;

                        await writer.WriteAsync(buffer.AsMemory(0, read), context.CancellationToken).ConfigureAwait(true);
                    }
                }

                status = ComponentValue.Ok();
            }
            catch (OperationCanceledException) when (writer.IsReaderDropped)
            {
                status = ComponentValue.Ok();
            }
            catch (Exception ex) when (ex is IOException or WasiException or NotSupportedException or UnauthorizedAccessException or ObjectDisposedException)
            {
                status = ComponentValue.Error(getError(ex));
            }
            finally
            {
                writer.Dispose();
            }

            result.Write(status);
        });

        return ComponentValue.FromTuple(readable, future);
    }

    /// <summary>Copies a <c>stream&lt;u8&gt;</c> to a .NET stream: returns the future of the result of the copy.</summary>
    private static ComponentValue WriteViaStream(ComponentCallContext context, ComponentValue data, Func<Stream?> open, Func<Exception, ComponentValue> getError)
    {
        var reader = data.AsStreamReader();
        var result = ComponentFutureWriter.Create(GetFutureType(context.Type), out var future);
        context.RunInBackground(async () =>
        {
            ComponentValue status;
            try
            {
                var stream = open();
                var buffer = new byte[BufferSize];
                while (!reader.IsCompleted)
                {
                    var read = await reader.ReadAsync(buffer, context.CancellationToken).ConfigureAwait(true);
                    if (read > 0 && stream is not null)
                    {
                        stream.Write(buffer, 0, read);
                        stream.Flush();
                    }
                }

                status = ComponentValue.Ok();
            }
            catch (Exception ex) when (ex is IOException or WasiException or NotSupportedException or UnauthorizedAccessException or ObjectDisposedException)
            {
                status = ComponentValue.Error(getError(ex));
            }
            finally
            {
                reader.Dispose();
            }

            result.Write(status);
        });

        return future;
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:cli
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentValue StdioError(Exception exception) => ComponentValue.FromEnum(exception is WasiException { Errno: WasiErrno.IllegalByteSequence } ? "illegal-byte-sequence" : exception is IOException ? "pipe" : "io");

    private void DefineCli(ComponentImports imports)
    {
        imports.Define(Name("wasi:cli/environment"), new ComponentImports()
            .Define("get-environment", Function((context, arguments) => Environment(_state.EnvironmentVariables)))
            .Define("get-arguments", Function((context, arguments) => Strings(_state.Arguments)))
            .Define("get-initial-cwd", Function((context, arguments) => ComponentValue.NoneValue)));

        imports.Define(Name("wasi:cli/exit"), new ComponentImports()
            .Define("exit", Function((context, arguments) => throw new WasiExitException(arguments[0].IsCase("ok") ? 0 : 1)))
            .Define("exit-with-code", Function((context, arguments) => throw new WasiExitException(arguments[0].AsU8()))));

        imports.Define(Name("wasi:cli/types"), new ComponentImports());
        imports.Define(Name("wasi:cli/stdin"), new ComponentImports()
            .Define("read-via-stream", Function((context, arguments) => ReadViaStream(context, () => _state.StandardInput, StdioError))));
        imports.Define(Name("wasi:cli/stdout"), new ComponentImports()
            .Define("write-via-stream", Function((context, arguments) => WriteViaStream(context, arguments[0], () => _state.StandardOutput, StdioError))));
        imports.Define(Name("wasi:cli/stderr"), new ComponentImports()
            .Define("write-via-stream", Function((context, arguments) => WriteViaStream(context, arguments[0], () => _state.StandardError, StdioError))));

        imports.Define(Name("wasi:cli/terminal-input"), new ComponentImports().Define("terminal-input", _terminalInput));
        imports.Define(Name("wasi:cli/terminal-output"), new ComponentImports().Define("terminal-output", _terminalOutput));
        imports.Define(Name("wasi:cli/terminal-stdin"), new ComponentImports().Define("get-terminal-stdin", Function((context, arguments) => ComponentValue.NoneValue)));
        imports.Define(Name("wasi:cli/terminal-stdout"), new ComponentImports().Define("get-terminal-stdout", Function((context, arguments) => ComponentValue.NoneValue)));
        imports.Define(Name("wasi:cli/terminal-stderr"), new ComponentImports().Define("get-terminal-stderr", Function((context, arguments) => ComponentValue.NoneValue)));
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:clocks and wasi:random
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentValue Instant(long seconds, uint nanoseconds) => ComponentValue.FromRecord(ComponentValue.FromS64(seconds), ComponentValue.FromU32(nanoseconds));

    private void DefineClocks(ComponentImports imports)
    {
        imports.Define(Name("wasi:clocks/types"), new ComponentImports());
        imports.Define(Name("wasi:clocks/monotonic-clock"), new ComponentImports()
            .Define("now", Function((context, arguments) => ComponentValue.FromU64(_state.MonotonicNow())))
            .Define("get-resolution", Function((context, arguments) => ComponentValue.FromU64(_state.MonotonicResolution)))
            .Define("wait-until", AsyncFunction(async (context, arguments) =>
            {
                await _state.DelayUntilAsync(arguments[0].AsU64(), context.CancellationToken).ConfigureAwait(true);
                return null;
            }))
            .Define("wait-for", AsyncFunction(async (context, arguments) =>
            {
                var now = _state.MonotonicNow();
                var duration = arguments[0].AsU64();
                await _state.DelayUntilAsync(duration > ulong.MaxValue - now ? ulong.MaxValue : now + duration, context.CancellationToken).ConfigureAwait(true);
                return null;
            })));

        imports.Define(Name("wasi:clocks/system-clock"), new ComponentImports()
            .Define("now", Function((context, arguments) =>
            {
                var (seconds, nanoseconds) = _state.SystemNow();
                return Instant(seconds, nanoseconds);
            }))
            .Define("get-resolution", Function((context, arguments) => ComponentValue.FromU64(100))));
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
            .Define("get-insecure-seed", Function((context, arguments) => ComponentValue.FromTuple(ComponentValue.FromU64(_state.InsecureSeed.Item1), ComponentValue.FromU64(_state.InsecureSeed.Item2)))));
    }

    // ------------------------------------------------------------------------------------------------------------
    // wasi:filesystem
    // ------------------------------------------------------------------------------------------------------------

    private static ComponentValue ErrorCode(Exception exception)
    {
        var code = WasiComponentFiles.GetErrorCode(WasiComponentFiles.GetErrno(exception));

        // WASI 0.3 has no would-block: the operations are asynchronous
        return code == "would-block" ? ComponentValue.FromCase("other", ComponentValue.Some(String(exception.Message))) : ComponentValue.FromCase(code);
    }

    private static ComponentValue Result(Func<ComponentValue?> operation)
    {
        try
        {
            return ComponentValue.Ok(operation());
        }
        catch (Exception ex) when (WasiComponentFiles.IsError(ex))
        {
            return ComponentValue.Error(ErrorCode(ex));
        }
    }

    private static ComponentValue Unit(Action operation) => Result(() =>
    {
        operation();
        return null;
    });

    private static ComponentValue GetType(WasiFileType type) => type switch
    {
        WasiFileType.BlockDevice => ComponentValue.FromCase("block-device"),
        WasiFileType.CharacterDevice => ComponentValue.FromCase("character-device"),
        WasiFileType.Directory => ComponentValue.FromCase("directory"),
        WasiFileType.RegularFile => ComponentValue.FromCase("regular-file"),
        WasiFileType.SymbolicLink => ComponentValue.FromCase("symbolic-link"),
        WasiFileType.SocketDatagram or WasiFileType.SocketStream => ComponentValue.FromCase("socket"),
        _ => ComponentValue.FromCase("other", ComponentValue.NoneValue),
    };

    private static ComponentValue Timestamp(DateTimeOffset value)
    {
        var ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var seconds = Math.DivRem(ticks, TimeSpan.TicksPerSecond, out var remainder);
        if (remainder < 0)
        {
            seconds--;
            remainder += TimeSpan.TicksPerSecond;
        }

        return ComponentValue.Some(Instant(seconds, (uint)(remainder * 100)));
    }

    private static ComponentValue Stat(WasiFileStat stat) => ComponentValue.FromRecord(
        GetType(stat.Type),
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

        var instant = value.Payload!.Value.AsRecord();
        var seconds = instant[0].AsS64();
        var nanoseconds = instant[1].AsU32();
        if (nanoseconds >= 1_000_000_000)
            throw new WasiException(WasiErrno.InvalidArgument);

        try
        {
            return DateTimeOffset.UnixEpoch.AddSeconds(seconds).AddTicks(nanoseconds / 100);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new WasiException(WasiErrno.Overflow);
        }
    }

    private static ComponentValue MetadataHash(WasiFileSystem fileSystem, WasiFileStat stat)
    {
        var (lower, upper) = WasiComponentFiles.MetadataHash(fileSystem, stat);
        return ComponentValue.FromRecord(ComponentValue.FromU64(lower), ComponentValue.FromU64(upper));
    }

    private static ComponentValue Flags(WasiFileDescriptor descriptor)
    {
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

        return ComponentValue.FromFlags(flags);
    }

    private static void Advise(WasiFileDescriptor descriptor)
    {
        if (descriptor.IsDirectory)
            throw new WasiException(WasiErrno.BadFileDescriptor);

        _ = descriptor.GetStream();
    }

    private static WasiFileRangeStream OpenForRead(WasiFileDescriptor descriptor, ulong offset)
    {
        if (offset > long.MaxValue)
            throw new WasiException(WasiErrno.InvalidArgument);

        var stream = descriptor.GetStream();
        if (!descriptor.CanRead)
            throw new WasiException(WasiErrno.BadFileDescriptor);

        return new WasiFileRangeStream(stream, (long)Math.Min(offset, long.MaxValue), append: false);
    }

    private static WasiFileRangeStream OpenForWrite(WasiFileDescriptor descriptor, ulong offset, bool append)
    {
        if (offset > long.MaxValue)
            throw new WasiException(WasiErrno.InvalidArgument);

        var stream = descriptor.GetStream();
        if (!descriptor.CanWrite)
            throw new WasiException(WasiErrno.BadFileDescriptor);

        return new WasiFileRangeStream(stream, (long)Math.Min(offset, long.MaxValue), append);
    }

    /// <summary>Writes the entries of a directory to a new <c>stream&lt;directory-entry&gt;</c>.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The background copy disposes the writer")]
    private static ComponentValue ReadDirectory(ComponentCallContext context, WasiFileDescriptor descriptor)
    {
        var (streamType, futureType) = GetStreamAndFutureTypes(context.Type);
        var writer = ComponentStreamWriter.Create(streamType, out var readable);
        var result = ComponentFutureWriter.Create(futureType, out var future);
        context.RunInBackground(async () =>
        {
            ComponentValue status;
            try
            {
                var entries = WasiComponentFiles.ReadDirectory(descriptor)
                    .Where(entry => entry.Name is not "." and not "..")
                    .Select(entry => ComponentValue.FromRecord(GetType(entry.Type), String(entry.Name)))
                    .ToArray();
                await writer.WriteAsync(entries, context.CancellationToken).ConfigureAwait(true);
                status = ComponentValue.Ok();
            }
            catch (Exception ex) when (WasiComponentFiles.IsError(ex))
            {
                status = ComponentValue.Error(ErrorCode(ex));
            }
            finally
            {
                writer.Dispose();
            }

            result.Write(status);
        });

        return ComponentValue.FromTuple(readable, future);
    }

    private void DefineFilesystem(ComponentImports imports)
    {
        WasiFileDescriptor D(ComponentValue value) => Get<WasiFileDescriptor>(value);

        var types = new ComponentImports()
            .Define("descriptor", _descriptor)
            .Define("[method]descriptor.read-via-stream", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var offset = arguments[1].AsU64();
                return ReadViaStream(context, () => OpenForRead(descriptor, offset), ErrorCode);
            }))
            .Define("[method]descriptor.write-via-stream", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                var offset = arguments[2].AsU64();
                return WriteViaStream(context, arguments[1], () => OpenForWrite(descriptor, offset, append: false), ErrorCode);
            }))
            .Define("[method]descriptor.append-via-stream", Function((context, arguments) =>
            {
                var descriptor = D(arguments[0]);
                return WriteViaStream(context, arguments[1], () => OpenForWrite(descriptor, 0, append: true), ErrorCode);
            }))
            .Define("[method]descriptor.read-directory", Function((context, arguments) => ReadDirectory(context, D(arguments[0]))))
            .Define("[method]descriptor.advise", Async(arguments => Unit(() => Advise(D(arguments[0])))))
            .Define("[method]descriptor.sync-data", Async(arguments => Unit(() => WasiComponentFiles.Sync(D(arguments[0])))))
            .Define("[method]descriptor.sync", Async(arguments => Unit(() => WasiComponentFiles.Sync(D(arguments[0])))))
            .Define("[method]descriptor.get-flags", Async(arguments => ComponentValue.Ok(Flags(D(arguments[0])))))
            .Define("[method]descriptor.get-type", Async(arguments => Result(() => GetType(WasiComponentFiles.Stat(D(arguments[0])).Type))))
            .Define("[method]descriptor.set-size", Async(arguments => Unit(() => WasiComponentFiles.SetSize(D(arguments[0]), arguments[1].AsU64()))))
            .Define("[method]descriptor.set-times", Async(arguments => Unit(() => WasiComponentFiles.SetTimes(D(arguments[0]), NewTimestamp(arguments[1]), NewTimestamp(arguments[2])))))
            .Define("[method]descriptor.create-directory-at", Async(arguments => Unit(() => WasiComponentFiles.CreateDirectoryAt(D(arguments[0]), arguments[1].AsString()))))
            .Define("[method]descriptor.stat", Async(arguments => Result(() => Stat(WasiComponentFiles.Stat(D(arguments[0]))))))
            .Define("[method]descriptor.stat-at", Async(arguments => Result(() => Stat(WasiComponentFiles.StatAt(D(arguments[0]), HasFlag(arguments[1], "symlink-follow"), arguments[2].AsString())))))
            .Define("[method]descriptor.set-times-at", Async(arguments => Unit(() => WasiComponentFiles.SetTimesAt(D(arguments[0]), HasFlag(arguments[1], "symlink-follow"), arguments[2].AsString(), NewTimestamp(arguments[3]), NewTimestamp(arguments[4])))))
            .Define("[method]descriptor.link-at", Async(arguments => Unit(() => WasiComponentFiles.LinkAt(D(arguments[0]), HasFlag(arguments[1], "symlink-follow"), arguments[2].AsString(), D(arguments[3]), arguments[4].AsString()))))
            .Define("[method]descriptor.open-at", Async(arguments =>
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
            .Define("[method]descriptor.readlink-at", Async(arguments => Result(() => String(WasiComponentFiles.ReadlinkAt(D(arguments[0]), arguments[1].AsString())))))
            .Define("[method]descriptor.remove-directory-at", Async(arguments => Unit(() => WasiComponentFiles.RemoveDirectoryAt(D(arguments[0]), arguments[1].AsString()))))
            .Define("[method]descriptor.rename-at", Async(arguments => Unit(() => WasiComponentFiles.RenameAt(D(arguments[0]), arguments[1].AsString(), D(arguments[2]), arguments[3].AsString()))))
            .Define("[method]descriptor.symlink-at", Async(arguments => Unit(() => WasiComponentFiles.SymlinkAt(D(arguments[0]), arguments[1].AsString(), arguments[2].AsString()))))
            .Define("[method]descriptor.unlink-file-at", Async(arguments => Unit(() => WasiComponentFiles.UnlinkFileAt(D(arguments[0]), arguments[1].AsString()))))
            .Define("[method]descriptor.is-same-object", Async(arguments => ComponentValue.FromBool(WasiComponentFiles.IsSameObject(D(arguments[0]), D(arguments[1])))))
            .Define("[method]descriptor.metadata-hash", Async(arguments =>
            {
                var descriptor = D(arguments[0]);
                return Result(() => MetadataHash(descriptor.FileSystem, WasiComponentFiles.Stat(descriptor)));
            }))
            .Define("[method]descriptor.metadata-hash-at", Async(arguments =>
            {
                var descriptor = D(arguments[0]);
                return Result(() => MetadataHash(descriptor.FileSystem, WasiComponentFiles.StatAt(descriptor, HasFlag(arguments[1], "symlink-follow"), arguments[2].AsString())));
            }));

        imports.Define(Name("wasi:filesystem/types"), types);
        imports.Define(Name("wasi:filesystem/preopens"), new ComponentImports()
            .Define("get-directories", Function((context, arguments) => ComponentValue.FromList(_state.Preopens.Select(preopen => ComponentValue.FromTuple(
                Resource(_descriptor, new WasiFileDescriptor(preopen.FileSystem, "", isDirectory: true, stream: null, canRead: true, canWrite: false, canMutate: !preopen.FileSystem.IsReadOnly)),
                String(preopen.GuestPath)))))));
    }
}
