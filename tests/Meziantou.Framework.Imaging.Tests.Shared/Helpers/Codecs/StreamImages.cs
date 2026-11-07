using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>Helpers shared by the writer and save tests: source images and an independent decoding of <see cref="TestStreamFormat"/> output.</summary>
internal static class StreamImages
{
    /// <summary>Creates single-frame images with distinct patterns and durations (the frames a writer borrows).</summary>
    public static Image<Rgba32>[] CreateFrames(int width, int height, int count)
    {
        var frames = new Image<Rgba32>[count];
        for (var i = 0; i < count; i++)
        {
            frames[i] = Image.ImportPixelBytes<Rgba32>(TestStreamFormat.Pattern(width, height, PixelFormat.Rgba32, i + 1), width, height);
            frames[i].Frames[0].Metadata.Duration = new FrameDuration(i + 1, 25);
        }

        return frames;
    }

    /// <summary>Creates an animated image with <paramref name="count"/> frames and optionally a poster.</summary>
    public static Image<Rgba32> CreateAnimation(int width, int height, int count, bool poster = false, int? totalPlays = null)
    {
        var frames = CreateFrames(width, height, count);
        var image = (Image<Rgba32>)frames[0].Clone();
        for (var i = 1; i < count; i++)
        {
            image.AppendFrame(frames[i].Frames[0]);
        }

        if (poster)
        {
            using var posterImage = Image.ImportPixelBytes<Rgba32>(TestStreamFormat.Pattern(width, height, PixelFormat.Rgba32, 99), width, height);
            image.SetPosterFrame(posterImage.Frames[0]);
        }

        if (count > 1 || poster || totalPlays is not null)
        {
            image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        }

        image.Metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "saved"));
        foreach (var frame in frames)
        {
            frame.Dispose();
        }

        return image;
    }

    /// <summary>Gets the pixel bytes of a frame.</summary>
    public static byte[] GetBytes(ImageFrame frame)
    {
        var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
        frame.CopyPixelBytesTo(bytes);
        return bytes;
    }

    /// <summary>Decodes test stream output with the test decoder (eager load), independently of the writer.</summary>
    public static Image Decode(byte[] data)
    {
        using var registry = Internals.ImageCodecRegistry.Override(new Internals.ImageCodecRegistry([new TestStreamCodec()]));
        return Image.Load(data);
    }
}
