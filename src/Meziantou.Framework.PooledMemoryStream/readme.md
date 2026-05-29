# Meziantou.Framework.PooledMemoryStream

`PooledMemoryStream` is a `MemoryStream` whose storage is a chain of **pooled byte arrays** rented from a process-wide pool shared by all instances. Growing the stream never reallocates and copies a single backing array — new pooled blocks are appended to the chain. Blocks are only ever rented in a small set of discrete sizes (Small / Medium / Large), so the pool stays effective. It also implements `IBufferWriter<byte>`.

## Basic usage

```csharp
using Meziantou.Framework;

using var stream = new PooledMemoryStream();
stream.Write("Hello, "u8);
stream.Write("World!"u8);

stream.Position = 0;
using var reader = new StreamReader(stream);
Console.WriteLine(reader.ReadToEnd()); // Hello, World!

// The pooled buffers are returned to the shared pool when the stream is disposed.
```

## Use it as an `IBufferWriter<byte>`

```csharp
using System.Buffers;
using System.Text.Json;
using Meziantou.Framework;

using var stream = new PooledMemoryStream();

// PooledMemoryStream implements IBufferWriter<byte> (explicit), so it can be passed where one is expected.
IBufferWriter<byte> writer = stream;
await using (var json = new Utf8JsonWriter(writer))
{
    json.WriteStartObject();
    json.WriteString("message", "hello");
    json.WriteEndObject();
}

var bytes = stream.ToArray(); // {"message":"hello"}
```

Data written through `IBufferWriter<byte>` is appended at the end of the stream; after `Advance`, `Position` is moved to the new end.

## Configure the buffer sizes

```csharp
using Meziantou.Framework;

var options = new PooledMemoryStreamOptions
{
    // Any number of strictly ascending tiers (bytes). Small streams use the smallest size and
    // grow through the larger ones; buffers bigger than the largest tier round up to a multiple of it.
    BufferSizes = [4 * 1024, 128 * 1024, 1024 * 1024, 10 * 1024 * 1024], // 4 KiB / 128 KiB / 1 MiB / 10 MiB
    MaxRetainedBytesPerBucket = 64L * 1024 * 1024,
    ClearOnReturn = false,
};

using var stream = new PooledMemoryStream(options);
```

`PooledMemoryStreamOptions` becomes immutable (frozen) the first time it is used to create a stream; after that, setting any property throws `InvalidOperationException`. The shared `PooledMemoryStreamOptions.Default` is always frozen. Configure all properties before passing the options to a stream.

## Notes

- The byte arrays (including the array returned by `GetBuffer()` / `TryGetBuffer()`, and the spans/memory returned by the `IBufferWriter<byte>` members) are owned by the stream and are returned to the shared pool on `Dispose`. **Do not use them after the stream is disposed.**
- The instance is not thread-safe (like `MemoryStream`); the shared pool is.
