namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>Provides extension methods for <see cref="SnapshotComparerCollection"/> to compare image snapshots by their pixels.</summary>
public static class SnapshotComparerCollectionImageComparerExtensions
{
    // Every file extension of a format Meziantou.Framework.Imaging decodes. "jpg" and "tif" are the same types as "jpeg" and "tiff".
    private static readonly SnapshotType[] ImageTypes =
    [
        SnapshotType.Png, "apng",
        SnapshotType.Jpeg,
        SnapshotType.Gif,
        SnapshotType.Webp,
        SnapshotType.Bmp, "dib",
        SnapshotType.Tiff,
        SnapshotType.Ico, ImageSnapshotTypes.Cur, ImageSnapshotTypes.Ani,
        ImageSnapshotTypes.Qoi,
        ImageSnapshotTypes.Tga, "icb", "vda", "vst",
        ImageSnapshotTypes.Pnm, "pbm", "pgm", "ppm", "pam",
        SnapshotType.Avif, "avifs",
        SnapshotType.Heic, "heif", "heics", "heifs",
        SnapshotType.JpegXl,
        SnapshotType.Jpeg2000, "j2k", "j2c", "jpc", "jpx", "jpf", "jph", "jhc",
    ];

    extension(SnapshotComparerCollection comparers)
    {
        /// <summary>
        /// Registers the <see cref="ImageComparer"/> for the snapshots of every format <c>Meziantou.Framework.Imaging</c>
        /// decodes: PNG and APNG, JPEG, GIF, WebP, BMP, TIFF, ICO, CUR, ANI, QOI, TGA, PNM (PBM, PGM, PPM and PAM), AVIF,
        /// HEIC, JPEG XL and JPEG 2000, under each of their file extensions. The PNG comparer also compares the frames that
        /// <c>AddGifSerializer</c> and <c>AddIcoSerializer</c> write.
        /// </summary>
        /// <param name="settings">Optional image comparison settings. When <see langword="null"/>, exact pixel comparison is used.</param>
        public void AddImageComparer(ImageComparisonSettings? settings = null)
        {
            ArgumentNullException.ThrowIfNull(comparers);

            var comparer = settings is null ? ImageComparer.Instance : new ImageComparer(settings);
            foreach (var type in ImageTypes)
            {
                comparers.Set(type, comparer);
            }
        }
    }
}
