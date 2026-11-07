namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>A tiny image: row-major pixels. Model is rgba, rgb, gray or graya; depth is 8 or 16.</summary>
internal sealed class Img
{
    public Img(int width, int height, string model, int depth, IEnumerable<Px> pixels)
    {
        Pixels = [.. pixels];
        Py.Assert(Pixels.Count == width * height, "pixel count");
        var channels = Channels(model);
        var maximum = (1 << depth) - 1;
        foreach (var p in Pixels)
            Py.Assert(p.Count == channels && p.All(v => v >= 0 && v <= maximum), $"{model} {depth} {p}");
        Width = width;
        Height = height;
        Model = model;
        Depth = depth;
    }

    public int Width { get; }

    public int Height { get; }

    public string Model { get; }

    public int Depth { get; }

    public List<Px> Pixels { get; }

    /// <summary>The pattern function that produced the image (fixture provenance).</summary>
    public string? PatternName { get; set; }

    public static int Channels(string model) => model switch
    {
        "rgba" => 4,
        "rgb" => 3,
        "gray" => 1,
        "graya" => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model),
    };

    /// <summary>Canonical straight-alpha RGBA at the same depth (gray replicated, missing alpha fully opaque).</summary>
    public List<Px> Rgba()
    {
        var maximum = (1 << Depth) - 1;
        Func<Px, Px> convert = Model switch
        {
            "rgba" => p => p,
            "rgb" => p => new Px(p[0], p[1], p[2], maximum),
            "gray" => p => new Px(p[0], p[0], p[0], maximum),
            "graya" => p => new Px(p[0], p[0], p[0], p[1]),
            _ => throw new InvalidOperationException(Model),
        };
        return [.. Pixels.Select(convert)];
    }

    /// <summary>Tightly packed, top-down raw bytes in an explicit layout.</summary>
    public byte[] Raw(string layout)
    {
        var b = new ByteBuilder();
        switch (layout)
        {
            case "rgba8":
                Py.Assert(Depth == 8);
                foreach (var p in Rgba())
                    foreach (var v in p)
                        b.U8(v);
                break;
            case "rgba16le":
                Py.Assert(Depth == 16);
                foreach (var p in Rgba())
                    foreach (var v in p)
                        b.U16LE(v);
                break;
            case "rgb8":
                Py.Assert(Depth == 8 && Model == "rgb");
                foreach (var p in Pixels)
                    foreach (var v in p)
                        b.U8(v);
                break;
            case "gray8":
                Py.Assert(Depth == 8 && Model == "gray");
                foreach (var p in Pixels)
                    b.U8(p[0]);
                break;
            case "gray16le":
                Py.Assert(Depth == 16 && Model == "gray");
                foreach (var p in Pixels)
                    b.U16LE(p[0]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layout), layout);
        }

        return b.ToArray();
    }

    /// <summary>Raw bytes and FFmpeg pixel format used as encoder input (big-endian for 16-bit PNG formats).</summary>
    public (byte[] Data, string Format) FfmpegRaw()
    {
        var b = new ByteBuilder();
        if (Depth == 8)
        {
            foreach (var p in Pixels)
                foreach (var v in p)
                    b.U8(v);
            return (b.ToArray(), Model switch { "rgba" => "rgba", "rgb" => "rgb24", "gray" => "gray", _ => "ya8" });
        }

        foreach (var p in Pixels)
            foreach (var v in p)
                b.U16BE(v);
        return (b.ToArray(), Model switch { "rgba" => "rgba64be", "rgb" => "rgb48be", "gray" => "gray16be", _ => "ya16be" });
    }
}
