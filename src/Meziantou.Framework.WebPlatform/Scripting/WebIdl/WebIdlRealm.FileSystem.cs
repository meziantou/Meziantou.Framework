using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The File System Standard (https://fs.spec.whatwg.org/): the members of the handles that the generated bindings do not
// define. FileSystemWritableFileStream is a WritableStream of Streams.js whose sink is the .NET stream; the async
// iterable declaration of FileSystemDirectoryHandle has no generated binding; the access handles read and write the
// buffer sources of the scripts in place; and remove(), move() and the locking modes are not in the standard yet.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>The interface object of FileSystemWritableFileStream (Streams.js), exposed in the secure contexts of the windows and the workers.</summary>
    private void DefineFileSystemGlobals()
    {
        Adapter.DefineLazyDataProperty(Global, Key("FileSystemWritableFileStream"), () => Adapter.Get(GetStreams(), Key("FileSystemWritableFileStream")), writable: true, enumerable: false, configurable: true);
    }

    /// <summary>Defines the members of the interfaces of the File System Standard that are implemented by the bindings.</summary>
    private void DefineFileSystemMembers(TValue prototype, string interfaceName)
    {
        switch (interfaceName)
        {
            case "FileSystemHandle":
                // The remove() method of the browsers (https://github.com/whatwg/fs/pull/9)
                DefineOperation(prototype, "remove", 0, static (realm, thisValue, arguments) =>
                {
                    var handle = realm.ThisFileSystemObject<FileSystemHandle>(thisValue, "FileSystemHandle");
                    var recursive = realm.GetBooleanMember(arguments.Length > 0 ? arguments[0] : realm.Adapter.Undefined, "recursive");
                    return realm.ToPromise(handle.Remove(new FileSystemRemoveOptions { Recursive = recursive }));
                }, returnsPromise: true);
                break;

            case "FileSystemFileHandle":
                // https://fs.spec.whatwg.org/#dom-filesystemfilehandle-createwritable
                DefineOperation(prototype, "createWritable", 0, static (realm, thisValue, arguments) =>
                {
                    var handle = realm.ThisFileSystemObject<FileSystemFileHandle>(thisValue, "FileSystemFileHandle");
                    var options = arguments.Length > 0 ? arguments[0] : realm.Adapter.Undefined;
                    var createOptions = new FileSystemCreateWritableOptions
                    {
                        KeepExistingData = realm.GetBooleanMember(options, "keepExistingData"),
                        Mode = realm.GetStringMember(options, "mode") ?? "siloed",
                    };
                    return realm.ToPromise(handle.CreateWritable(createOptions), static (realm, stream) => realm.CreateFileSystemWritableFileStream(stream));
                }, returnsPromise: true);

                // The move() method of the browsers (https://github.com/whatwg/fs/pull/10): move(name), move(directory) and
                // move(directory, name)
                DefineOperation(prototype, "move", 1, static (realm, thisValue, arguments) =>
                {
                    var handle = realm.ThisFileSystemObject<FileSystemFileHandle>(thisValue, "FileSystemFileHandle");
                    if (arguments.Length < 1)
                        throw realm.NotEnoughArguments("move", 1, arguments.Length);

                    if (realm.Unwrap(arguments[0]) is FileSystemDirectoryHandle directory)
                        return realm.ToPromise(handle.Move(directory, arguments.Length > 1 && !realm.Adapter.IsUndefined(arguments[1]) ? realm.ToUsvString(arguments[1]) : null));

                    return realm.ToPromise(handle.Move(realm.ToUsvString(arguments[0])));
                }, returnsPromise: true);

                // https://fs.spec.whatwg.org/#dom-filesystemfilehandle-createsyncaccesshandle, with the mode option of Chromium
                if (IsExposed(ExposureSet.DedicatedWorker))
                {
                    DefineOperation(prototype, "createSyncAccessHandle", 0, static (realm, thisValue, arguments) =>
                    {
                        var handle = realm.ThisFileSystemObject<FileSystemFileHandle>(thisValue, "FileSystemFileHandle");
                        var mode = realm.GetStringMember(arguments.Length > 0 ? arguments[0] : realm.Adapter.Undefined, "mode");
                        return realm.ToPromise(handle.CreateSyncAccessHandle(new FileSystemCreateSyncAccessHandleOptions { Mode = mode ?? "readwrite" }), static (realm, accessHandle) => realm.GetWrapper(accessHandle));
                    }, returnsPromise: true);
                }

                break;

            case "FileSystemDirectoryHandle":
                DefineFileSystemDirectoryIteration(prototype);
                break;

            case "FileSystemSyncAccessHandle":
                // https://fs.spec.whatwg.org/#dom-filesystemsyncaccesshandle-read and write: the bytes of the buffer source are
                // read and written in place
                DefineOperation(prototype, "read", 1, static (realm, thisValue, arguments) =>
                {
                    var (accessHandle, buffer, options) = realm.ToAccessHandleArguments(thisValue, arguments, "read");
                    return realm.Adapter.FromNumber(accessHandle.Read(buffer.Bytes.Span, options));
                });
                DefineOperation(prototype, "write", 1, static (realm, thisValue, arguments) =>
                {
                    var (accessHandle, buffer, options) = realm.ToAccessHandleArguments(thisValue, arguments, "write");
                    return realm.Adapter.FromNumber(accessHandle.Write(buffer.Bytes.Span, options));
                });

                // The mode attribute of the locking proposal that Chromium implements
                DefineAttribute(prototype, "mode", static (realm, thisValue) => realm.Adapter.FromString(realm.ThisFileSystemObject<FileSystemSyncAccessHandle>(thisValue, "FileSystemSyncAccessHandle").Mode), setter: null);
                break;
        }
    }

    private T ThisFileSystemObject<T>(TValue thisValue, string interfaceName)
        where T : class
        => Unwrap(thisValue) as T ?? throw CreateTypeError($"Illegal invocation: 'this' does not implement interface {interfaceName}");

    private (FileSystemSyncAccessHandle AccessHandle, JsBufferSource Buffer, FileSystemReadWriteOptions? Options) ToAccessHandleArguments(TValue thisValue, ReadOnlySpan<TValue> arguments, string operation)
    {
        var accessHandle = ThisFileSystemObject<FileSystemSyncAccessHandle>(thisValue, "FileSystemSyncAccessHandle");
        if (arguments.Length < 1)
            throw NotEnoughArguments(operation, 1, arguments.Length);

        var buffer = ToBufferSource(arguments[0], AllBufferSourceKinds, allowShared: true, "AllowSharedBufferSource");
        FileSystemReadWriteOptions? options = null;
        if (arguments.Length > 1 && !Adapter.IsUndefined(arguments[1]) && !Adapter.IsNull(arguments[1]))
        {
            if (!Adapter.IsObject(arguments[1]))
                throw CreateTypeError("The options are not an object");

            // [EnforceRange] unsigned long long at
            var at = Adapter.Get(arguments[1], Key("at"));
            if (!Adapter.IsUndefined(at))
            {
                options = new FileSystemReadWriteOptions { At = ToUnsignedLongLong(at, IntegerConversion.EnforceRange) };
            }
        }

        return (accessHandle, buffer, options);
    }

    /// <summary>The conversion of a boolean member of a dictionary (false when the member is missing).</summary>
    private bool GetBooleanMember(TValue dictionary, string name)
    {
        if (Adapter.IsUndefined(dictionary) || Adapter.IsNull(dictionary))
            return false;

        if (!Adapter.IsObject(dictionary))
            throw CreateTypeError("The options are not an object");

        var value = Adapter.Get(dictionary, Key(name));
        return !Adapter.IsUndefined(value) && Adapter.ToBoolean(value);
    }

    /// <summary>The conversion of a string member of a dictionary (null when the member is missing).</summary>
    private string? GetStringMember(TValue dictionary, string name)
    {
        if (Adapter.IsUndefined(dictionary) || Adapter.IsNull(dictionary))
            return null;

        if (!Adapter.IsObject(dictionary))
            throw CreateTypeError("The options are not an object");

        var value = Adapter.Get(dictionary, Key(name));
        return Adapter.IsUndefined(value) ? null : Adapter.ToString(value);
    }

    /// <summary>
    /// Creates the FileSystemWritableFileStream of a .NET stream (createFileSystemWritableFileStream of Streams.js): the
    /// write algorithm gives the command of the chunk to the stream, the close algorithm replaces the content of the file
    /// and the abort algorithm discards the buffer.
    /// </summary>
    private TValue CreateFileSystemWritableFileStream(FileSystemWritableFileStream stream)
    {
        var adapter = Adapter;
        var sink = CreateObject();
        SetMember(sink, "mode", adapter.FromString(stream.Mode));
        SetMember(sink, "isBlob", adapter.CreateFunction("isBlob", 1, (_, args) => adapter.FromBoolean(Unwrap(args[0]) is Blob)));
        SetMember(sink, "write", adapter.CreateFunction("write", 4, (_, args) =>
        {
            var type = adapter.ToString(args[0]);
            var data = args[1];
            var positionValue = args[2];
            var sizeValue = args[3];
            return Invoke(() =>
            {
                long? position = adapter.IsNull(positionValue) ? null : ToFilePosition(adapter.ToNumber(positionValue));
                long? size = adapter.IsNull(sizeValue) ? null : ToFilePosition(adapter.ToNumber(sizeValue));
                // https://fs.spec.whatwg.org/#write-a-chunk: the specification rejects a command without its member with a
                // TypeError; the browsers all reject with a SyntaxError DOMException (and with a TypeError for a null data,
                // as the web-platform-tests expect)
                switch (type)
                {
                    case "write" when adapter.IsUndefined(data):
                        throw DomException.Syntax("The write command requires the 'data' member");
                    case "write" when adapter.IsNull(data):
                        throw new TypeErrorException("The 'data' member of the write command is null");
                    case "seek" when position is null:
                        throw DomException.Syntax("The seek command requires the 'position' member");
                    case "truncate" when size is null:
                        throw DomException.Syntax("The truncate command requires the 'size' member");
                    case "write":
                        if (Unwrap(data) is Blob blob)
                        {
                            // "Let dataBytes be the result of performing the read operation on data. If this throws an
                            // exception, reject p with that exception": the snapshot of a file that was removed is not readable
                            if (blob is Dom.FileApi.File { IsSnapshotReadable: { } isReadable } && !isReadable())
                                throw DomException.NotFound("The file of the blob does not exist");

                            stream.Write(blob.Data.Span, position);
                        }
                        else if (adapter.IsString(data))
                        {
                            stream.Write(System.Text.Encoding.UTF8.GetBytes(ToUsvString(data)), position);
                        }
                        else
                        {
                            stream.Write(ToBufferSource(data, BufferSourceKinds, allowShared: false, "BufferSource").Bytes.Span, position);
                        }

                        break;

                    case "seek":
                        stream.Seek(position!.Value);
                        break;

                    default:
                        stream.Truncate(size!.Value);
                        break;
                }
            });
        }));
        SetMember(sink, "close", adapter.CreateFunction("close", 0, (_, _) => Invoke(stream.Close)));
        SetMember(sink, "abort", adapter.CreateFunction("abort", 0, (_, _) => Invoke(stream.Abort)));
        return adapter.Call(adapter.Get(GetStreams(), Key("createFileSystemWritableFileStream")), adapter.Undefined, [sink]);

        // A position that the buffer of the stream cannot have exceeds the quota
        static long ToFilePosition(double value) => value > Array.MaxLength ? throw new QuotaExceededError("The file is too large") : (long)value;

        TValue Invoke(Action action)
        {
            try
            {
                action();
                return adapter.Undefined;
            }
            catch (Exception ex) when (!adapter.TryGetThrownValue(ex, out TValue? _))
            {
                throw ToJsException(ex);
            }
        }
    }

    /// <summary>The deserialization steps of a FileSystemHandle (https://fs.spec.whatwg.org/#filesystemhandle-deserialization-steps), in this realm.</summary>
    private TValue DeserializeFileSystemHandle(SerializedFileSystemHandle serialized)
    {
        try
        {
            return GetWrapper(serialized.Deserialize(GlobalScope));
        }
        catch (DomException ex)
        {
            throw Adapter.CreateException(ToJsError(ex));
        }
    }

    // https://webidl.spec.whatwg.org/#es-asynchronous-iterable: entries(), keys(), values() and @@asyncIterator of a pair
    // asynchronously iterable declaration, and its asynchronous iterator prototype object. next(iterator state) is the "get
    // the next iteration result" algorithm: it returns a promise of a [key, value] pair, or of undefined at the end.
    private const string AsyncIterableScript = """
        (function (interfaceName, brandCheck, createState, getNext) {
          'use strict';
          const AsyncIteratorPrototype = Object.getPrototypeOf(Object.getPrototypeOf(async function* () {}).prototype);
          const apply = Reflect.apply;
          const promiseThen = Promise.prototype.then;
          const Promise_ = Promise;
          const promiseReject = Promise.reject;
          const weakMapGet = WeakMap.prototype.get;
          const weakMapSet = WeakMap.prototype.set;
          const iterators = new WeakMap();
          const then = (promise, onFulfilled, onRejected) => apply(promiseThen, promise, [onFulfilled, onRejected]);
          const iteratorPrototype = Object.create(AsyncIteratorPrototype);
          const methods = {
            next() {
              const iterator = (typeof this === 'object' && this !== null) ? apply(weakMapGet, iterators, [this]) : undefined;
              if (iterator === undefined) return apply(promiseReject, Promise_, [new TypeError(`Illegal invocation: 'this' is not a ${interfaceName} async iterator`)]);
              const nextSteps = () => {
                if (iterator.isFinished) return new Promise_((resolve) => resolve({ value: undefined, done: true }));
                let nextPromise;
                try {
                  nextPromise = new Promise_((resolve) => resolve(getNext(iterator.target, iterator.state)));
                } catch (error) {
                  nextPromise = apply(promiseReject, Promise_, [error]);
                }

                return then(nextPromise, (next) => {
                  iterator.ongoingPromise = null;
                  if (next === undefined) {
                    iterator.isFinished = true;
                    return { value: undefined, done: true };
                  }

                  const value = iterator.kind === 'key' ? next[0] : iterator.kind === 'value' ? next[1] : [next[0], next[1]];
                  return { value, done: false };
                }, (reason) => {
                  iterator.ongoingPromise = null;
                  iterator.isFinished = true;
                  throw reason;
                });
              };

              const ongoingPromise = iterator.ongoingPromise;
              iterator.ongoingPromise = ongoingPromise !== null ? then(ongoingPromise, nextSteps, nextSteps) : nextSteps();
              return iterator.ongoingPromise;
            },
          };
          Object.defineProperty(iteratorPrototype, 'next', { value: methods.next, writable: true, enumerable: true, configurable: true });
          Object.defineProperty(iteratorPrototype, Symbol.toStringTag, { value: `${interfaceName} AsyncIterator`, writable: false, enumerable: false, configurable: true });
          function create(target, kind) {
            brandCheck(target);
            const object = Object.create(iteratorPrototype);
            apply(weakMapSet, iterators, [object, { target, kind, state: createState(target), ongoingPromise: null, isFinished: false }]);
            return object;
          }

          return {
            entries() { return create(this, 'key+value'); },
            keys() { return create(this, 'key'); },
            values() { return create(this, 'value'); },
          };
        })
        """;

    /// <summary>https://fs.spec.whatwg.org/#api-filesystemdirectoryhandle-asynciterable: <c>async_iterable&lt;USVString, FileSystemHandle&gt;</c>.</summary>
    private void DefineFileSystemDirectoryIteration(TValue prototype)
    {
        var adapter = Adapter;
        var brandCheck = adapter.CreateFunction("brandCheck", 1, (_, args) =>
        {
            ThisFileSystemObject<FileSystemDirectoryHandle>(args[0], "FileSystemDirectoryHandle");
            return adapter.Undefined;
        });

        // The state of an iterator: its past results ("Set iterator's past results to an empty set")
        var createState = adapter.CreateFunction("createState", 1, (_, _) =>
        {
            var state = CreateObject();
            Agent.SerializedPlatformObjects.AddOrUpdate(state, new HashSet<string>(StringComparer.Ordinal));
            return state;
        });
        var getNext = adapter.CreateFunction("getNext", 2, (_, args) =>
        {
            var handle = (FileSystemDirectoryHandle)Unwrap(args[0])!;
            var pastResults = Agent.SerializedPlatformObjects.TryGetValue(args[1], out var value) ? (HashSet<string>)value! : [];
            return ToPromise(handle.GetNextEntry(pastResults), static (realm, entry) => entry is { } pair
                ? realm.Adapter.CreateArray([realm.Adapter.FromString(pair.Key), realm.GetWrapper(pair.Value)])
                : realm.Adapter.Undefined);
        });
        var methods = adapter.Call(EvaluateBuiltIn(AsyncIterableScript), adapter.Undefined, [Key("FileSystemDirectoryHandle"), brandCheck, createState, getNext]);
        var entries = adapter.Get(methods, Key("entries"));
        DefineMethod(prototype, Key("entries"), entries, enumerable: true);
        DefineMethod(prototype, Key("keys"), adapter.Get(methods, Key("keys")), enumerable: true);
        DefineMethod(prototype, Key("values"), adapter.Get(methods, Key("values")), enumerable: true);
        DefineMethod(prototype, adapter.GetWellKnownSymbol(JsWellKnownSymbol.AsyncIterator), entries, enumerable: false);
    }
}
