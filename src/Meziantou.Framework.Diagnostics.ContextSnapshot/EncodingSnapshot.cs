using System.Text;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

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
