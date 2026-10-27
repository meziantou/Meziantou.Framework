namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the dithering applied while reducing colors to the palette of an indexed BMP layout.</summary>
/// <remarks>Replaced by <see cref="DitheringMode"/>, which <see cref="BmpEncoder.Quantization"/> takes: its members have the same values.</remarks>
[Obsolete("Use DitheringMode with BmpEncoder.Quantization.")]
public enum BmpDithering
{
    /// <summary>No dithering: each pixel maps to the nearest palette entry. This is the default.</summary>
    None = 0,

    /// <summary>Floyd–Steinberg error diffusion, from the top row down whatever the row order (deterministic for fixed settings).</summary>
    FloydSteinberg = 1,
}
