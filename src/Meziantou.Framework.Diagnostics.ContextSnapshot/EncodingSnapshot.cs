namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a text encoding including name and read-only status.</summary>
public sealed class EncodingSnapshot
{
    internal EncodingSnapshot(Encoding encoding)
    {
        Name = encoding.EncodingName;
        WebName = encoding.WebName;
        IsReadOnly = encoding.IsReadOnly;
    }

    public string Name { get; }
    public string WebName { get; }
    public bool IsReadOnly { get; }
}
