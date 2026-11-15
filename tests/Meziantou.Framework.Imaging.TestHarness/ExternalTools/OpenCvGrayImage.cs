namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>An 8-bit single-channel image, row by row.</summary>
public sealed class OpenCvGrayImage
{
    public OpenCvGrayImage(int width, int height, byte[] samples)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(samples.Length, width * height);
        Width = width;
        Height = height;
        Samples = samples;
    }

    public int Width { get; }

    public int Height { get; }

#pragma warning disable CA1819 // Properties should not return arrays: a buffer of samples
    public byte[] Samples { get; }
#pragma warning restore CA1819

    public byte this[int x, int y]
    {
        get => Samples[(y * Width) + x];
        set => Samples[(y * Width) + x] = value;
    }
}
