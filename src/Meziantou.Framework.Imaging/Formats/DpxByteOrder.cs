namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The byte order of the fields and of the image data of a DPX file written by <see cref="DpxEncoder"/>.</summary>
public enum DpxByteOrder
{
    /// <summary>Most significant byte first (magic number <c>SDPX</c>), the order the standard asks writers to use. This is the default.</summary>
    BigEndian = 0,

    /// <summary>Least significant byte first (magic number <c>XPDS</c>).</summary>
    LittleEndian = 1,
}
