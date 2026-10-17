using System.Globalization;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>The container description printed by <c>webpmux -info</c> (libwebp's own container parser).</summary>
public sealed class WebPMuxInfo
{
    /// <summary>Gets the canvas width.</summary>
    public int Width { get; private init; }

    /// <summary>Gets the canvas height.</summary>
    public int Height { get; private init; }

    /// <summary>Gets the features listed by webpmux (for example <c>animation</c>, <c>transparency</c>, <c>ICC profile</c>, <c>EXIF metadata</c>, <c>XMP metadata</c>).</summary>
    public string Features { get; private init; } = "";

    /// <summary>Gets the loop count of an animation, or <see langword="null"/> for a still image.</summary>
    public int? LoopCount { get; private init; }

    /// <summary>Gets the background color of an animation as printed (for example <c>0x00000000</c>).</summary>
    public string? BackgroundColor { get; private init; }

    /// <summary>Gets the frames of an animation (empty for a still image).</summary>
    public IReadOnlyList<WebPMuxFrame> Frames { get; private init; } = [];

    /// <summary>Parses the output.</summary>
    /// <param name="text">The standard output of <c>webpmux -info</c>.</param>
    /// <returns>The description.</returns>
    public static WebPMuxInfo Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int width = 0, height = 0;
        int? loop = null;
        string? background = null;
        var features = "";
        var frames = new List<WebPMuxFrame>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("Canvas size:", StringComparison.Ordinal))
            {
                var parts = line["Canvas size:".Length..].Split('x', StringSplitOptions.TrimEntries);
                width = int.Parse(parts[0], CultureInfo.InvariantCulture);
                height = int.Parse(parts[1], CultureInfo.InvariantCulture);
            }
            else if (line.StartsWith("Features present:", StringComparison.Ordinal))
            {
                features = line["Features present:".Length..].Trim();
            }
            else if (line.StartsWith("Background color", StringComparison.Ordinal))
            {
                // "Background color : 0x00000000  Loop Count : 3"
                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                background = tokens[3];
                loop = int.Parse(tokens[^1], CultureInfo.InvariantCulture);
            }
            else if (line.Length > 0 && char.IsAsciiDigit(line[0]) && line.Contains(':', StringComparison.Ordinal))
            {
                // "1:     6     4    no        0        0      100       none    no        64     lossless"
                var tokens = line.Split([' ', ':'], StringSplitOptions.RemoveEmptyEntries);
                frames.Add(new WebPMuxFrame(
                    Width: int.Parse(tokens[1], CultureInfo.InvariantCulture),
                    Height: int.Parse(tokens[2], CultureInfo.InvariantCulture),
                    HasAlpha: tokens[3] == "yes",
                    X: int.Parse(tokens[4], CultureInfo.InvariantCulture),
                    Y: int.Parse(tokens[5], CultureInfo.InvariantCulture),
                    DurationMilliseconds: int.Parse(tokens[6], CultureInfo.InvariantCulture),
                    Dispose: tokens[7],
                    Blend: tokens[8] == "yes",
                    Compression: tokens[^1]));
            }
        }

        return new WebPMuxInfo { Width = width, Height = height, Features = features, LoopCount = loop, BackgroundColor = background, Frames = frames };
    }
}
