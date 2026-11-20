using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>The type of an <see cref="EncodedAudioChunk"/> (https://w3c.github.io/webcodecs/#enumdef-encodedaudiochunktype).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-encodedaudiochunktype"/>
[IdlInterface("EncodedAudioChunkType", "https://w3c.github.io/webcodecs/#enumdef-encodedaudiochunktype")]
public enum EncodedAudioChunkType
{
    /// <summary>A key chunk: it can be decoded without the previous chunks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunktype-key"/>
    [IdlMember("key", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunktype-key")]
    Key,

    /// <summary>A delta chunk: it depends on the previous chunks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunktype-delta"/>
    [IdlMember("delta", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunktype-delta")]
    Delta,
}

/// <summary>The description of an <see cref="EncodedAudioChunk"/> (https://w3c.github.io/webcodecs/#dictdef-encodedaudiochunkinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-encodedaudiochunkinit"/>
[IdlInterface("EncodedAudioChunkInit", "https://w3c.github.io/webcodecs/#dictdef-encodedaudiochunkinit")]
public sealed class EncodedAudioChunkInit
{
    /// <summary>Gets the type of the chunk.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-type"/>
    [IdlMember("type", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-type")]
    public required EncodedAudioChunkType Type { get; init; }

    /// <summary>Gets the presentation timestamp in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-timestamp")]
    public required long Timestamp { get; init; }

    /// <summary>Gets the presentation duration in microseconds, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-duration"/>
    [IdlMember("duration", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-duration")]
    public ulong? Duration { get; init; }

    /// <summary>Gets the encoded bytes: a buffer source of the scripts, a <see cref="byte"/> array or a memory of bytes.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-data"/>
    [IdlMember("data", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-data")]
    public required object Data { get; init; }

    /// <summary>Gets the <c>ArrayBuffer</c> objects detached by the construction.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-transfer"/>
    [IdlMember("transfer", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunkinit-transfer")]
    public IReadOnlyList<object>? Transfer { get; init; }
}

/// <summary>
/// A chunk of encoded audio (https://w3c.github.io/webcodecs/#encodedaudiochunk): the input of a <see cref="AudioDecoder"/> and the
/// output of a <see cref="AudioEncoder"/>. Its bytes are immutable; it can be posted to a worker (serialized).
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#encodedaudiochunk"/>
[IdlInterface("EncodedAudioChunk", "https://w3c.github.io/webcodecs/#encodedaudiochunk")]
public sealed class EncodedAudioChunk : IAgentClusterSerializable
{
    private readonly byte[] _data;

    /// <summary>Creates a chunk (https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-encodedaudiochunk): the bytes are copied.</summary>
    /// <param name="init">The type, the timestamp and the bytes of the chunk.</param>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the transfer list has a detached buffer or a buffer twice.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-encodedaudiochunk"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-encodedaudiochunk")]
    public EncodedAudioChunk(EncodedAudioChunkInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        var bytes = WebCodecsBuffers.GetBytes(init.Data);
        var transfer = WebCodecsBuffers.ValidateTransfer(init.Transfer);
        _data = bytes.ToArray();
        WebCodecsBuffers.DetachTransfer(transfer);
        Type = init.Type;
        Timestamp = init.Timestamp;
        Duration = init.Duration;
    }

    private EncodedAudioChunk(EncodedAudioChunk chunk)
    {
        _data = chunk._data;
        Type = chunk.Type;
        Timestamp = chunk.Timestamp;
        Duration = chunk.Duration;
    }

    /// <summary>Gets the type of the chunk.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-type"/>
    [IdlMember("type", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-type")]
    public EncodedAudioChunkType Type { get; }

    /// <summary>Gets the presentation timestamp in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-timestamp")]
    public long Timestamp { get; }

    /// <summary>Gets the presentation duration in microseconds, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-duration"/>
    [IdlMember("duration", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-duration")]
    public ulong? Duration { get; }

    /// <summary>Gets the number of encoded bytes.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-bytelength"/>
    [IdlMember("byteLength", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-bytelength")]
    public uint ByteLength => (uint)_data.Length;

    /// <summary>Gets the encoded bytes.</summary>
    [NotInIdl("The .NET access to the encoded bytes (the scripts use copyTo())")]
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>Copies the encoded bytes into a buffer (https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-copyto).</summary>
    /// <param name="destination">The buffer.</param>
    /// <exception cref="TypeErrorException">The buffer is smaller than the chunk.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-copyto"/>
    [IdlMember("copyTo", "https://w3c.github.io/webcodecs/#dom-encodedaudiochunk-copyto")]
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _data.Length)
            throw new TypeErrorException("The destination is smaller than the chunk");

        _data.CopyTo(destination);
    }

    /// <summary>The serialization steps (https://w3c.github.io/webcodecs/#encodedaudiochunk-serialization): the serialized chunk shares the bytes.</summary>
    object IStructuredSerializable.CloneForStructuredSerialization() => new EncodedAudioChunk(this);
}

/// <summary>The type of an <see cref="EncodedVideoChunk"/> (https://w3c.github.io/webcodecs/#enumdef-encodedvideochunktype).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#enumdef-encodedvideochunktype"/>
[IdlInterface("EncodedVideoChunkType", "https://w3c.github.io/webcodecs/#enumdef-encodedvideochunktype")]
public enum EncodedVideoChunkType
{
    /// <summary>A key chunk: it can be decoded without the previous chunks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunktype-key"/>
    [IdlMember("key", "https://w3c.github.io/webcodecs/#dom-encodedvideochunktype-key")]
    Key,

    /// <summary>A delta chunk: it depends on the previous chunks.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunktype-delta"/>
    [IdlMember("delta", "https://w3c.github.io/webcodecs/#dom-encodedvideochunktype-delta")]
    Delta,
}

/// <summary>The description of an <see cref="EncodedVideoChunk"/> (https://w3c.github.io/webcodecs/#dictdef-encodedvideochunkinit).</summary>
/// <seealso href="https://w3c.github.io/webcodecs/#dictdef-encodedvideochunkinit"/>
[IdlInterface("EncodedVideoChunkInit", "https://w3c.github.io/webcodecs/#dictdef-encodedvideochunkinit")]
public sealed class EncodedVideoChunkInit
{
    /// <summary>Gets the type of the chunk.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-type"/>
    [IdlMember("type", "https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-type")]
    public required EncodedVideoChunkType Type { get; init; }

    /// <summary>Gets the presentation timestamp in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-timestamp")]
    public required long Timestamp { get; init; }

    /// <summary>Gets the presentation duration in microseconds, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-duration"/>
    [IdlMember("duration", "https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-duration")]
    public ulong? Duration { get; init; }

    /// <summary>Gets the encoded bytes: a buffer source of the scripts, a <see cref="byte"/> array or a memory of bytes.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-data"/>
    [IdlMember("data", "https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-data")]
    public required object Data { get; init; }

    /// <summary>Gets the <c>ArrayBuffer</c> objects detached by the construction.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-transfer"/>
    [IdlMember("transfer", "https://w3c.github.io/webcodecs/#dom-encodedvideochunkinit-transfer")]
    public IReadOnlyList<object>? Transfer { get; init; }
}

/// <summary>
/// A chunk of encoded video (https://w3c.github.io/webcodecs/#encodedvideochunk): the input of a <see cref="VideoDecoder"/> and the
/// output of a <see cref="VideoEncoder"/>. Its bytes are immutable; it can be posted to a worker (serialized).
/// </summary>
/// <seealso href="https://w3c.github.io/webcodecs/#encodedvideochunk"/>
[IdlInterface("EncodedVideoChunk", "https://w3c.github.io/webcodecs/#encodedvideochunk")]
public sealed class EncodedVideoChunk : IAgentClusterSerializable
{
    private readonly byte[] _data;

    /// <summary>Creates a chunk (https://w3c.github.io/webcodecs/#dom-encodedvideochunk-encodedvideochunk): the bytes are copied.</summary>
    /// <param name="init">The type, the timestamp and the bytes of the chunk.</param>
    /// <exception cref="DomException">A <c>DataCloneError</c> when the transfer list has a detached buffer or a buffer twice.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunk-encodedvideochunk"/>
    [IdlMember("constructor", "https://w3c.github.io/webcodecs/#dom-encodedvideochunk-encodedvideochunk")]
    public EncodedVideoChunk(EncodedVideoChunkInit init)
    {
        ArgumentNullException.ThrowIfNull(init);
        var bytes = WebCodecsBuffers.GetBytes(init.Data);
        var transfer = WebCodecsBuffers.ValidateTransfer(init.Transfer);
        _data = bytes.ToArray();
        WebCodecsBuffers.DetachTransfer(transfer);
        Type = init.Type;
        Timestamp = init.Timestamp;
        Duration = init.Duration;
    }

    private EncodedVideoChunk(EncodedVideoChunk chunk)
    {
        _data = chunk._data;
        Type = chunk.Type;
        Timestamp = chunk.Timestamp;
        Duration = chunk.Duration;
    }

    /// <summary>Gets the type of the chunk.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunk-type"/>
    [IdlMember("type", "https://w3c.github.io/webcodecs/#dom-encodedvideochunk-type")]
    public EncodedVideoChunkType Type { get; }

    /// <summary>Gets the presentation timestamp in microseconds.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunk-timestamp"/>
    [IdlMember("timestamp", "https://w3c.github.io/webcodecs/#dom-encodedvideochunk-timestamp")]
    public long Timestamp { get; }

    /// <summary>Gets the presentation duration in microseconds, or null.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunk-duration"/>
    [IdlMember("duration", "https://w3c.github.io/webcodecs/#dom-encodedvideochunk-duration")]
    public ulong? Duration { get; }

    /// <summary>Gets the number of encoded bytes.</summary>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunk-bytelength"/>
    [IdlMember("byteLength", "https://w3c.github.io/webcodecs/#dom-encodedvideochunk-bytelength")]
    public uint ByteLength => (uint)_data.Length;

    /// <summary>Gets the encoded bytes.</summary>
    [NotInIdl("The .NET access to the encoded bytes (the scripts use copyTo())")]
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>Copies the encoded bytes into a buffer (https://w3c.github.io/webcodecs/#dom-encodedvideochunk-copyto).</summary>
    /// <param name="destination">The buffer.</param>
    /// <exception cref="TypeErrorException">The buffer is smaller than the chunk.</exception>
    /// <seealso href="https://w3c.github.io/webcodecs/#dom-encodedvideochunk-copyto"/>
    [IdlMember("copyTo", "https://w3c.github.io/webcodecs/#dom-encodedvideochunk-copyto")]
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _data.Length)
            throw new TypeErrorException("The destination is smaller than the chunk");

        _data.CopyTo(destination);
    }

    /// <summary>The serialization steps (https://w3c.github.io/webcodecs/#encodedvideochunk-serialization): the serialized chunk shares the bytes.</summary>
    object IStructuredSerializable.CloneForStructuredSerialization() => new EncodedVideoChunk(this);
}
