namespace Meziantou.Framework.Email;

/// <summary>The options of the MIME reader. The limits protect it from messages crafted to make it use too much memory or time.</summary>
public sealed class MimeReadOptions
{
    internal static MimeReadOptions Default { get; } = new();

    /// <summary>Gets or sets the maximum depth of the multiparts and of the embedded messages. A deeper entity is read as a leaf part. The default is 64.</summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>Gets or sets the maximum number of parts of a multipart. The default is 10,000.</summary>
    public int MaxParts { get; set; } = 10_000;

    /// <summary>Gets or sets the maximum size, in bytes, of a message read from a stream. The default is 1 GB.</summary>
    public long MaxSize { get; set; } = 1024L * 1024 * 1024;
}
