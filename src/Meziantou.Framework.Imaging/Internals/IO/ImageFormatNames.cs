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
        _ => "image",
    };
}
