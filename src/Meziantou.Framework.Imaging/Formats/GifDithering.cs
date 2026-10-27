namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the dithering applied while reducing colors to a GIF palette.</summary>
/// <remarks>Replaced by <see cref="DitheringMode"/>, which <see cref="GifEncoder.Quantization"/> takes: its members have the same values.</remarks>
[Obsolete("Use DitheringMode with GifEncoder.Quantization.")]
public enum GifDithering
{
    /// <summary>No dithering: each pixel maps to the nearest palette entry. This is the default.</summary>
    None = 0,

    /// <summary>Floyd–Steinberg error diffusion (deterministic for fixed settings).</summary>
    FloydSteinberg = 1,
}
