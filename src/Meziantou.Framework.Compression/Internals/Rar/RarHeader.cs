namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>A header of a RAR archive (RAR 1.5-4.x or RAR5).</summary>
internal class RarHeader
{
    public RarHeaderKind Kind { get; init; }

    /// <summary>Gets or sets the size of the data that follow the header.</summary>
    public long DataSize { get; set; }

    /// <summary>Gets or sets the position of the data that follow the header, in the volume.</summary>
    public long DataOffset { get; set; }
}
