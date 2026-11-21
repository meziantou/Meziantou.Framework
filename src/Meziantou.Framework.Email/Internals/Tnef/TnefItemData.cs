namespace Meziantou.Framework.Email.Internals.Tnef;

/// <summary>The content of a TNEF stream: the properties of the message, of its recipients and of its attachments.</summary>
internal sealed class TnefItemData
{
    public List<TnefProperty> Properties { get; } = [];

    public List<List<TnefProperty>> Recipients { get; } = [];

    public List<List<TnefProperty>> Attachments { get; } = [];

    /// <summary>Gets or sets the code page of the 8-bit strings (attOemCodepage).</summary>
    public int CodePage { get; set; } = 1252;
}
