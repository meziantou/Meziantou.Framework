namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The channels a texture format stores, which decide the channels whose loss needs a setting.</summary>
internal enum TextureChannels
{
    /// <summary>One channel, read as gray (red is stored).</summary>
    Gray,

    /// <summary>Red and green, read with a blue of 0.</summary>
    RedGreen,

    /// <summary>Red, green and blue, read opaque.</summary>
    Rgb,

    /// <summary>Every channel.</summary>
    Rgba,
}
