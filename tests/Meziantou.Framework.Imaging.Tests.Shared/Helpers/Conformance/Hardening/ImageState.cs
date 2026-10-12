using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

/// <summary>
/// A structural and pixel snapshot of an image (canvas, frame identities, order, sizes, durations and pixels, poster, the
/// orientation and resolution metadata), used to prove that failed transactional operations changed nothing and that failed
/// pixel edits left a structurally valid image.
/// </summary>
internal sealed record ImageState(string Description)
{
    public static ImageState Capture(Image image)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{image.Width}x{image.Height} {image.PixelFormat} orientation={image.Metadata.Orientation} resolution={image.Metadata.Resolution} animated={image.IsAnimated}");
        for (var i = 0; i < image.Frames.Count; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"; frame {i} #{RuntimeHelpers.GetHashCode(image.Frames[i])} {Describe(image.Frames[i])}");
        }

        if (image.PosterFrame is { } poster)
        {
            builder.Append(CultureInfo.InvariantCulture, $"; poster #{RuntimeHelpers.GetHashCode(poster)} {Describe(poster)}");
        }

        return new ImageState(builder.ToString());
    }

    /// <summary>Reads every pixel of every frame (throws if the image is not structurally valid).</summary>
    public static void AssertReadable(Image image)
    {
        foreach (var frame in image.Frames)
        {
            Assert.Equal(image.Size, frame.Size);
            _ = Describe(frame);
        }
    }

    private static string Describe(ImageFrame frame)
    {
        var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
        frame.CopyPixelBytesTo(bytes);
        return string.Create(CultureInfo.InvariantCulture, $"{frame.Width}x{frame.Height} {frame.Metadata.Duration} {Convert.ToHexString(SHA256.HashData(bytes))[..16]}");
    }
}
