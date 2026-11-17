using Meziantou.Framework.Imaging.TestHarness;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The RGBA8 images the block encoder tests compress, block by block: a photo of the corpus and synthetic content (smooth
/// gradients, noise, sharp edges, solid blocks, alpha), generated with a fixed LCG so that the error bounds the tests
/// assert are reproducible.
/// </summary>
internal static class BlockEncoderTestImages
{
    /// <summary>Gets an image by name: its width and height are multiples of 4.</summary>
    public static TestImage Get(string name)
    {
        var palette = BlockColors();
        return name switch
        {
            "photo" => Photo(),
            "photo+alpha" => WithAlpha(Photo(), (x, y) => (x * 7) + (y * 3)),
            "gradient" => Generate(64, 64, (x, y) => (x * 4, y * 4, (x + y) * 2, 255)),
            "noise" => Noise(withAlpha: false),
            "noise+alpha" => Noise(withAlpha: true),
            "edges" => Generate(64, 64, (x, y) =>
            {
                var block = ((y / 4) * 16) + (x / 4);
                var color = ((x + y) % 4) < 2 ? palette[block % 256] : palette[(block + 7) % 256];
                return (color.R, color.G, color.B, 255);
            }),
            "solid" => Generate(64, 64, (x, y) =>
            {
                var color = palette[((y / 4) * 16) + (x / 4)];
                return (color.R, color.G, color.B, 255);
            }),
            "solid+alpha" => Generate(64, 64, (x, y) => palette[((y / 4) * 16) + (x / 4)]),
            "alpha-gradient" => Generate(64, 64, (x, y) => (x * 4, 128, y * 4, (x * 2) + (y * 2))),
            "cutout" => Generate(64, 64, (x, y) => (x * 4, y * 4, 200, ((x - 32) * (x - 32)) + ((y - 32) * (y - 32)) < 400 ? 255 : 0)),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, message: null),
        };
    }

    /// <summary>The photo of the QOI corpus (33 × 17), padded to 36 × 20 by repeating its last column and row, as the containers pad partial blocks.</summary>
    private static TestImage Photo()
    {
        var path = RepositoryPaths.Root / "tests" / "Meziantou.Framework.Imaging.Fixtures" / "qoi" / "ref-rgb-photo.frame-0.rgba8.raw";
        var source = File.ReadAllBytes(path);
        const int SourceWidth = 33;
        const int SourceHeight = 17;
        return Generate(36, 20, (x, y) =>
        {
            var offset = ((Math.Min(y, SourceHeight - 1) * SourceWidth) + Math.Min(x, SourceWidth - 1)) * 4;
            return (source[offset], source[offset + 1], source[offset + 2], source[offset + 3]);
        });
    }

    private static TestImage WithAlpha(TestImage image, Func<int, int, int> alpha)
    {
        var rgba = (byte[])image.Rgba.Clone();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                rgba[(((y * image.Width) + x) * 4) + 3] = (byte)alpha(x, y);
            }
        }

        return image with { Rgba = rgba };
    }

    private static TestImage Noise(bool withAlpha)
    {
        // The opaque noise comes first in the sequence of the generator, then the noise with alpha
        var random = new Lcg(1);
        var opaque = Generate(64, 64, (x, y) => (random.Next(), random.Next(), random.Next(), 255));
        return withAlpha ? Generate(64, 64, (x, y) => (random.Next(), random.Next(), random.Next(), random.Next())) : opaque;
    }

    /// <summary>256 random RGBA colors, one per block of the 64 × 64 images.</summary>
    private static (int R, int G, int B, int A)[] BlockColors()
    {
        var random = new Lcg(2);
        var colors = new (int R, int G, int B, int A)[256];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = (random.Next(), random.Next(), random.Next(), random.Next());
        }

        return colors;
    }

    private static TestImage Generate(int width, int height, Func<int, int, (int R, int G, int B, int A)> texel)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = texel(x, y);
                var offset = ((y * width) + x) * 4;
                rgba[offset] = (byte)r;
                rgba[offset + 1] = (byte)g;
                rgba[offset + 2] = (byte)b;
                rgba[offset + 3] = (byte)a;
            }
        }

        return new TestImage(width, height, rgba);
    }

    /// <summary>An RGBA8 image, row-major.</summary>
    internal sealed record TestImage(int Width, int Height, byte[] Rgba)
    {
        public int BlockCount => Width / 4 * (Height / 4);

        /// <summary>Copies the 16 texels of a block (row-major, as the block encoders read them).</summary>
        public void CopyBlock(int index, Span<byte> texels)
        {
            var blockX = index % (Width / 4);
            var blockY = index / (Width / 4);
            for (var y = 0; y < 4; y++)
            {
                Rgba.AsSpan(((((blockY * 4) + y) * Width) + (blockX * 4)) * 4, 16).CopyTo(texels[(y * 16)..]);
            }
        }
    }

    /// <summary>
    /// Gets 64 blocks of pseudo-random content for the determinism tests: noise, smooth blocks, two-color blocks and blocks
    /// of one color, with varying alpha.
    /// </summary>
    public static byte[] DeterminismBlocks()
    {
        var random = new Lcg(1234);
        var texels = new byte[64 * 64];
        for (var block = 0; block < 64; block++)
        {
            var kind = block % 4;
            int r0 = random.Next(), g0 = random.Next(), b0 = random.Next(), a0 = random.Next();
            int r1 = random.Next(), g1 = random.Next(), b1 = random.Next(), a1 = random.Next();
            for (var i = 0; i < 16; i++)
            {
                var texel = texels.AsSpan((block * 64) + (i * 4), 4);
                switch (kind)
                {
                    case 0:
                        texel[0] = random.Next();
                        texel[1] = random.Next();
                        texel[2] = random.Next();
                        texel[3] = random.Next();
                        break;
                    case 1:
                        texel[0] = (byte)(r0 + ((r1 - r0) * i / 15));
                        texel[1] = (byte)(g0 + ((g1 - g0) * (i % 4) / 3));
                        texel[2] = (byte)(b0 + ((b1 - b0) * (i / 4) / 3));
                        texel[3] = block % 8 == 1 ? (byte)255 : (byte)(a0 + ((a1 - a0) * i / 15));
                        break;
                    case 2:
                        var second = (i % 4) >= 2;
                        texel[0] = (byte)(second ? r1 : r0);
                        texel[1] = (byte)(second ? g1 : g0);
                        texel[2] = (byte)(second ? b1 : b0);
                        texel[3] = (byte)(second ? a1 : a0);
                        break;
                    default:
                        texel[0] = (byte)r0;
                        texel[1] = (byte)g0;
                        texel[2] = (byte)b0;
                        texel[3] = (byte)a0;
                        break;
                }
            }
        }

        return texels;
    }

    /// <summary>A linear congruential generator (Numerical Recipes constants): the same sequence on every platform.</summary>
    internal sealed class Lcg(uint seed)
    {
        private uint _state = seed;

        /// <summary>Gets the next byte: the high byte of the state.</summary>
        public byte Next()
        {
            _state = (_state * 1664525) + 1013904223;
            return (byte)(_state >> 24);
        }
    }
}
