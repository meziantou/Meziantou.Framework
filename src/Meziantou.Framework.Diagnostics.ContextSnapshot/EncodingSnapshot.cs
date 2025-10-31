namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of text encoding information at a specific point in time.</summary>
public sealed class EncodingSnapshot
{
    internal EncodingSnapshot(Encoding encoding)
    {
        Name = encoding.EncodingName;
        WebName = encoding.WebName;
        IsReadOnly = encoding.IsReadOnly;
    }

    /// <summary>Gets the human-readable name of the encoding.</summary>
    public string Name { get; }
    /// <summary>Gets the IANA-registered name of the encoding.</summary>
    public string WebName { get; }
    /// <summary>Gets a value indicating whether the encoding is read-only.</summary>
    public bool IsReadOnly { get; }
}
