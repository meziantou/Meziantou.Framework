namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The supplemental enhancement information messages that the decoder reads; the other ones are skipped.</summary>
internal sealed class HevcSeiMessages
{
    /// <summary>Gets or sets the hash of the picture that the last suffix SEI NAL unit described.</summary>
    public HevcDecodedPictureHash? DecodedPictureHash { get; set; }

    public HevcMasteringDisplayColourVolume? MasteringDisplayColourVolume { get; set; }

    public HevcContentLightLevel? ContentLightLevel { get; set; }

    public HevcAlphaChannelInfo? AlphaChannelInfo { get; set; }
}
