namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An item of the <c>meta</c> box of an AVIF file (ISO/IEC 14496-12, section 8.11): its type (<c>iinf</c>), the location of its
/// data (<c>iloc</c>) and the properties associated with it (<c>ipma</c>).
/// </summary>
internal sealed class HeifItem
{
    public HeifItem(uint id)
    {
        Id = id;
    }

    public uint Id { get; }

    /// <summary>Gets or sets the type of the item, 0 when the item has no <c>infe</c> entry.</summary>
    public uint Type { get; set; }

    public bool IsHidden { get; set; }

    /// <summary>Gets or sets a value indicating whether the item is a <c>mime</c> item whose content type is XMP.</summary>
    public bool IsXmp { get; set; }

    /// <summary>Gets or sets the construction method of the item data: 0 for offsets in the file, 1 for offsets in the <c>idat</c> box.</summary>
    public int ConstructionMethod { get; set; }

    public bool HasLocation { get; set; }

    /// <summary>Gets the extents of the item data, in order: the offset includes the base offset of the item.</summary>
    public List<(long Offset, long Length)> Extents { get; } = [];

    /// <summary>Gets the properties associated with the item, in order: the 1-based index of the property in <c>ipco</c> and whether it is essential.</summary>
    public List<(int Index, bool IsEssential)> Properties { get; } = [];

    /// <summary>Gets the number of bytes of the item data.</summary>
    public long DataLength
    {
        get
        {
            long length = 0;
            foreach (var extent in Extents)
            {
                length = checked(length + extent.Length);
            }

            return length;
        }
    }
}
