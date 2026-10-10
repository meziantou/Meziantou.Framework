namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>An immutable sequence of metadata bytes (EXIF, XMP, ICC...).</summary>
/// <remarks>
/// The constructor copies its input, so the caller cannot mutate the blob afterward. Because blobs are immutable, metadata
/// clones share them instead of copying them. Blobs created by callers are validated when they are serialized, not when
/// they are constructed, and are caller allocations rather than part of an image allocation budget. Equality compares the
/// bytes.
/// </remarks>
public sealed class MetadataBlob : IEquatable<MetadataBlob>
{
    private readonly byte[] _data;

    /// <summary>Initializes a new instance of the <see cref="MetadataBlob"/> class by copying the specified bytes.</summary>
    /// <param name="data">The bytes to copy.</param>
    public MetadataBlob(ReadOnlySpan<byte> data) => _data = data.ToArray();

    private MetadataBlob(byte[] data) => _data = data;

    /// <summary>Gets an empty blob.</summary>
    internal static MetadataBlob Empty { get; } = new([]);

    /// <summary>Adopts an array owned by the library without copying it. The caller must not keep or modify the array.</summary>
    /// <param name="data">The array to adopt.</param>
    /// <returns>The blob.</returns>
    internal static MetadataBlob FromOwnedArray(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new MetadataBlob(data);
    }

    /// <summary>Gets the number of bytes.</summary>
    public int Length => _data.Length;

    /// <summary>Gets the bytes as read-only memory.</summary>
    public ReadOnlyMemory<byte> Memory => _data;

    /// <summary>Gets the bytes as a read-only span.</summary>
    public ReadOnlySpan<byte> Span => _data;

    /// <summary>Copies the bytes to a new array.</summary>
    /// <returns>A new array containing a copy of the bytes.</returns>
    public byte[] ToArray() => (byte[])_data.Clone();

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] MetadataBlob? other) => other is not null && (ReferenceEquals(_data, other._data) || _data.AsSpan().SequenceEqual(other._data));

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as MetadataBlob);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_data);
        return hash.ToHashCode();
    }
}
