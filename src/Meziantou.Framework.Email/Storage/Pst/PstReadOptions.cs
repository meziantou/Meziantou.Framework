namespace Meziantou.Framework.Email.Storage;

/// <summary>The options of <see cref="PstFile"/>. The limits protect the reader from files crafted to make it use too much memory or time.</summary>
public sealed class PstReadOptions
{
    internal static PstReadOptions Default { get; } = new();

    /// <summary>Gets or sets whether the reader checks the CRC of the pages and the blocks it reads. The default is <see langword="true"/>.</summary>
    public bool VerifyChecksums { get; set; } = true;

    /// <summary>Gets or sets the maximum number of levels of the B-trees (pages, data trees, subnode trees, heaps). The default is 32.</summary>
    public int MaxTreeDepth { get; set; } = 32;

    /// <summary>Gets or sets the maximum depth of embedded messages (a message attached to a message). The default is 32.</summary>
    public int MaxEmbeddedMessageDepth { get; set; } = 32;

    /// <summary>Gets or sets the maximum size, in bytes, of a property value or a structure the reader loads in memory. The default is 256 MB. Attachment contents read with <see cref="MapiAttachment.OpenRead"/> are not limited.</summary>
    public long MaxPropertySize { get; set; } = 256L * 1024 * 1024;

    /// <summary>Gets or sets the maximum number of pages and blocks kept in memory. The default is 256.</summary>
    public int CacheSize { get; set; } = 256;
}
