namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Display names of formats used in exception messages.</summary>
internal static class ImageFormatNames
{
    public static string Get(ImageFormat format) => format switch
    {
        ImageFormat.Png => "PNG",
        ImageFormat.Gif => "GIF",
        ImageFormat.Jpeg => "JPEG",
        ImageFormat.WebP => "WebP",
        ImageFormat.Qoi => "QOI",
        ImageFormat.Bmp => "BMP",
        ImageFormat.Tga => "TGA",
        ImageFormat.Pnm => "PNM",
        ImageFormat.Tiff => "TIFF",
        ImageFormat.Ico => "ICO",
        ImageFormat.Cur => "CUR",
        ImageFormat.Ani => "ANI",
        ImageFormat.Avif => "AVIF",
        ImageFormat.JpegXl => "JPEG XL",
        ImageFormat.Heic => "HEIC",
        ImageFormat.Jpeg2000 => "JPEG 2000",
        ImageFormat.OpenExr => "OpenEXR",
        ImageFormat.Radiance => "Radiance HDR",
        ImageFormat.Psd => "PSD",
        ImageFormat.Dng => "DNG",
        ImageFormat.Dds => "DDS",
        ImageFormat.Dpx => "DPX",
        ImageFormat.Cineon => "Cineon",
        ImageFormat.Ktx2 => "KTX 2.0",
        ImageFormat.Custom => "custom",
        _ => "image",
    };
}
