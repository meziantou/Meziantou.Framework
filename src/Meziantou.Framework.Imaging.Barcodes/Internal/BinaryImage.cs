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

    /// <summary>Gets whether the pixel under a point is dark.</summary>
    public bool IsDark(PointD point) => IsDark(point.X, point.Y);

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

    /// <summary>
    /// Classifies the pixels with a threshold per block of 8x8 pixels: the mean of the 5x5 blocks around it, where the
    /// blocks have contrast. A block of even luminance takes the threshold of its neighbors, or half its minimum (it is
    /// light) when they have none, so a blank area does not turn into noise. It follows the shadows and the gradients of
    /// photographs, at the scale of a few modules.
    /// </summary>
    public static BinaryImage CreateLocalBlocks(Image<Gray8> luminance, CancellationToken cancellationToken)
    {
        const int BlockSize = 8;
        const int MinimumContrast = 24;

        var width = luminance.Width;
        var height = luminance.Height;
        var pixels = new Gray8[width * height];
        luminance.Frames[0].CopyPixelDataTo(pixels);
        var blocksX = (width + BlockSize - 1) / BlockSize;
        var blocksY = (height + BlockSize - 1) / BlockSize;

        // The black point of each block: the mean where the block has contrast
        var blackPoints = new int[blocksX * blocksY];
        for (var by = 0; by < blocksY; by++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var bx = 0; bx < blocksX; bx++)
            {
                // The last blocks overlap the previous ones to stay in the image
                var x0 = Math.Max(0, Math.Min(bx * BlockSize, width - BlockSize));
                var y0 = Math.Max(0, Math.Min(by * BlockSize, height - BlockSize));
                var x1 = Math.Min(width, x0 + BlockSize);
                var y1 = Math.Min(height, y0 + BlockSize);
                var sum = 0;
                var min = 255;
                var max = 0;
                for (var y = y0; y < y1; y++)
                {
                    var row = y * width;
                    for (var x = x0; x < x1; x++)
                    {
                        int value = pixels[row + x].Value;
                        sum += value;
                        min = Math.Min(min, value);
                        max = Math.Max(max, value);
                    }
                }

                var count = (x1 - x0) * (y1 - y0);
                var average = sum / count;
                if (max - min <= MinimumContrast)
                {
                    // An even block is assumed light, unless its neighbors have a black point above its minimum: then it
                    // is part of a dark area (a large module) and takes their threshold
                    average = min / 2;
                    if (by > 0 && bx > 0)
                    {
                        var neighbors = (blackPoints[((by - 1) * blocksX) + bx] + (2 * blackPoints[(by * blocksX) + bx - 1]) + blackPoints[((by - 1) * blocksX) + bx - 1]) / 4;
                        if (min < neighbors)
                        {
                            average = neighbors;
                        }
                    }
                }

                blackPoints[(by * blocksX) + bx] = average;
            }
        }

        // Each pixel is dark when it is at most the mean of the black points of the 5x5 blocks around its block
        var dark = new bool[width * height];
        for (var by = 0; by < blocksY; by++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var top = Math.Clamp(by, 2, Math.Max(2, blocksY - 3));
            for (var bx = 0; bx < blocksX; bx++)
            {
                var left = Math.Clamp(bx, 2, Math.Max(2, blocksX - 3));
                var sum = 0;
                var count = 0;
                for (var dy = -2; dy <= 2; dy++)
                {
                    var ny = top + dy;
                    if ((uint)ny >= (uint)blocksY)
                        continue;

                    for (var dx = -2; dx <= 2; dx++)
                    {
                        var nx = left + dx;
                        if ((uint)nx >= (uint)blocksX)
                            continue;

                        sum += blackPoints[(ny * blocksX) + nx];
                        count++;
                    }
                }

                var threshold = sum / count;
                var x0 = bx * BlockSize;
                var y0 = by * BlockSize;
                var x1 = Math.Min(width, x0 + BlockSize);
                var y1 = Math.Min(height, y0 + BlockSize);
                for (var y = y0; y < y1; y++)
                {
                    var row = y * width;
                    for (var x = x0; x < x1; x++)
                    {
                        dark[row + x] = pixels[row + x].Value <= threshold;
                    }
                }
            }
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
