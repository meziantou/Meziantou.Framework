namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>The pixels of an image classified as dark or light, for the locators of the symbols.</summary>
internal sealed class BinaryImage
{
    private readonly bool[] _dark;

    private BinaryImage(int width, int height, bool[] dark)
    {
        Width = width;
        Height = height;
        _dark = dark;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets whether a pixel is dark: the pixels outside the image are light.</summary>
    public bool IsDark(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height && _dark[(y * Width) + x];

    /// <summary>Gets whether the pixel under a point is dark.</summary>
    public bool IsDark(double x, double y) => IsDark((int)Math.Floor(x), (int)Math.Floor(y));

    /// <summary>Gets the image with the dark and light pixels swapped, for symbols printed light on a dark background.</summary>
    public BinaryImage Invert()
    {
        var dark = new bool[_dark.Length];
        for (var i = 0; i < dark.Length; i++)
        {
            dark[i] = !_dark[i];
        }

        return new BinaryImage(Width, Height, dark);
    }

    /// <summary>Gets the luminance of the first frame of an image, a transparent background being white.</summary>
    public static Image<Gray8> GetLuminance(Image image)
    {
        var options = new PixelConversionOptions
        {
            BackgroundColor = Rgba64.FromRgba32(Colors.White),
            DiscardIncompatibleColorProfile = true,
        };

        if (image.Frames.Count > 1)
        {
            using var firstFrame = image.CloneFrame(0);
            return firstFrame.CloneAs<Gray8>(options);
        }

        return image.CloneAs<Gray8>(options);
    }

    /// <summary>Classifies the pixels with a threshold method of the imaging library.</summary>
    public static BinaryImage Create(Image<Gray8> luminance, ThresholdOptions options, CancellationToken cancellationToken)
    {
        using var thresholded = luminance.CloneAs<Gray8>();
        thresholded.Threshold(options, cancellationToken);
        var width = thresholded.Width;
        var height = thresholded.Height;
        var pixels = new Gray8[width * height];
        thresholded.Frames[0].CopyPixelDataTo(pixels);
        var dark = new bool[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            dark[i] = pixels[i].Value < 128;
        }

        return new BinaryImage(width, height, dark);
    }

    /// <summary>Gets the runs of a row: alternating light and dark widths, starting with a light run (possibly 0 wide).</summary>
    public int[] GetRowRuns(int y) => GetRuns(Width, x => IsDark(x, y));

    /// <summary>Gets the runs of a column, from the top.</summary>
    public int[] GetColumnRuns(int x) => GetRuns(Height, y => IsDark(x, y));

    private static int[] GetRuns(int length, Func<int, bool> isDark)
    {
        var runs = new List<int> { 0 };
        var dark = false;
        for (var i = 0; i < length; i++)
        {
            var value = isDark(i);
            if (value != dark)
            {
                runs.Add(0);
                dark = value;
            }

            runs[^1]++;
        }

        return [.. runs];
    }
}
