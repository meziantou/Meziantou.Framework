namespace Meziantou.Framework.Imaging;

/// <summary>What an image stored next to the primary image of a file is (<see cref="AuxiliaryImage.Kind"/>).</summary>
public enum AuxiliaryImageKind
{
    /// <summary>An image of another kind: an auxiliary image of a type that is not recognized, or another image of a JPEG file.</summary>
    Other = 0,

    /// <summary>A smaller version of the primary image (a HEIF <c>thmb</c> reference, a large thumbnail of a JPEG Multi-Picture Format file).</summary>
    Thumbnail,

    /// <summary>A depth or disparity map (the depth auxiliary type of HEVC or MPEG-B).</summary>
    Depth,

    /// <summary>
    /// A gain map, which turns the primary image into its high dynamic range rendition (ISO 21496-1, Ultra HDR, the gain
    /// map auxiliary type of Apple).
    /// </summary>
    GainMap,

    /// <summary>A segmentation matte of Apple (portrait effects, skin, hair, teeth, glasses, sky).</summary>
    Matte,
}
