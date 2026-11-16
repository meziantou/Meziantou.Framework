using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Layered still JPEG XL images (<see cref="JpegXlLayeredImage"/>) and animations written as the regions that change
/// (<see cref="JpegXlEncoder.OptimizeFrames"/>): the layers round trip through the library decoder, exactly for lossless
/// output, the image they make is the one the blend modes define, and the arguments are validated. djxl, jxl-oxide and
/// jxlinfo read the outputs in the interop job.
/// </summary>
public sealed class JpegXlLayeredImageTests
{
    [Fact]
    public void Usage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"layers-{Guid.NewGuid():N}.jxl");
        try
        {
            #region JpegXlLayeredImage_Usage
            using (var document = new JpegXlLayeredImage(64, 48))
            {
                // The bottom layer covers the canvas; a smaller layer is drawn over it at an offset, weighted by its alpha
                using var background = new Image<Rgba32>(64, 48, new Rgba32(255, 255, 255, 255));
                using var badge = new Image<Rgba32>(16, 16, new Rgba32(200, 0, 0, 255));
                document.AddLayer(background, name: "Background");
                document.AddLayer(badge, new Point(40, 4), JpegXlBlendMode.Blend, "Badge");
                document.Save(path);
            }

            // A viewer shows the image the layers make...
            using (var image = Image.Load<Rgba32>(path))
            {
                Assert.Equal(new Rgba32(200, 0, 0, 255), image.Frames[0][45, 10]);
                Assert.Equal(new Rgba32(255, 255, 255, 255), image.Frames[0][10, 10]);
            }

            // ...and the layers are read back, with their positions, blend modes and names
            using (var document = JpegXlLayeredImage.Load(path))
            {
                Assert.Equal(2, document.Layers.Count);
                Assert.Equal((new Point(40, 4), JpegXlBlendMode.Blend, "Badge"), (document.Layers[1].Offset, document.Layers[1].BlendMode, document.Layers[1].Name));
                Assert.Equal(new Size(16, 16), document.Layers[1].Image.Size);
            }
            #endregion
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32, 0)]
    [InlineData(PixelFormat.Rgba32, 3)]
    [InlineData(PixelFormat.Rgba32, 7)]
    [InlineData(PixelFormat.Bgra32, 5)]
    [InlineData(PixelFormat.Rgba64, 5)]
    [InlineData(PixelFormat.Rgb24, 1)]
    [InlineData(PixelFormat.Rgb24, 5)]
    [InlineData(PixelFormat.Gray8, 4)]
    [InlineData(PixelFormat.Gray16, 0)]
    public void LosslessLayersRoundTripExactly(PixelFormat format, int effort)
    {
        // Layers smaller and larger than the canvas, at negative offsets and beyond it, of more than one group, with every mode
        using var document = new JpegXlLayeredImage(300, 200);
        AddLayer(document, format, 300, 200, seed: 1, default, JpegXlBlendMode.Replace, "Background");
        AddLayer(document, format, 40, 30, seed: 2, new Point(-10, -5), JpegXlBlendMode.Blend, "Top left");
        AddLayer(document, format, 290, 270, seed: 3, new Point(60, 20), JpegXlBlendMode.Add, null);
        AddLayer(document, format, 7, 3, seed: 4, new Point(299, 199), JpegXlBlendMode.Multiply, "Corner");
        AddLayer(document, format, 50, 60, seed: 5, new Point(100, 100), JpegXlBlendMode.AlphaWeightedAdd, "Équipe ✓");
        var data = Save(document, new JpegXlEncoder { Effort = effort });

        using var decoded = JpegXlLayeredImage.Load(data);
        AssertSameLayers(document, decoded);
        Assert.Equal(new Size(300, 200), decoded.Size);

        // The image the layers make is a still image of the size of the canvas
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal((300, 200, 1, false), (info.Size.Width, info.Size.Height, info.FrameCount, info.IsAnimated));
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Gray16)]
    public void ProgressiveLayersRoundTripExactly(PixelFormat format)
    {
        using var document = new JpegXlLayeredImage(140, 90);
        AddLayer(document, format, 140, 90, seed: 1, default, JpegXlBlendMode.Replace, null);
        AddLayer(document, format, 300, 20, seed: 2, new Point(-80, 30), JpegXlBlendMode.Blend, "Band");
        var data = Save(document, new JpegXlEncoder { Progressive = true });

        using var decoded = JpegXlLayeredImage.Load(data);
        AssertSameLayers(document, decoded);
    }

    /// <summary>The image of opaque layers that replace the pixels below them is the last layer that covers each pixel, over transparent black.</summary>
    [Theory]
    [InlineData(JpegXlBlendMode.Replace)]
    [InlineData(JpegXlBlendMode.Blend)]
    public void OpaqueLayersAreDrawnOverTheLayersBelow(JpegXlBlendMode mode)
    {
        using var document = new JpegXlLayeredImage(20, 10);
        using var first = new Image<Rgba32>(8, 6, new Rgba32(10, 20, 30, 255));
        using var second = new Image<Rgba32>(30, 3, new Rgba32(200, 100, 50, 255));
        document.AddLayer(first, new Point(2, 1));
        document.AddLayer(second, new Point(-5, 4), mode);

        using var image = Image.Load<Rgba32>(Save(document, new JpegXlEncoder()));
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                var expected = y is >= 4 and < 7 ? new Rgba32(200, 100, 50, 255) : x is >= 2 and < 10 && y is >= 1 and < 7 ? new Rgba32(10, 20, 30, 255) : default;
                Assert.Equal(expected, image.Frames[0][x, y]);
            }
        }
    }

    [Fact]
    public void AddedLayersAddTheirSamples()
    {
        using var document = new JpegXlLayeredImage(4, 4);
        using var first = new Image<Rgb24>(4, 4, new Rgb24(100, 200, 3));
        using var second = new Image<Rgb24>(2, 2, new Rgb24(20, 100, 4));
        document.AddLayer(first);
        document.AddLayer(second, new Point(1, 1), JpegXlBlendMode.Add);

        using var image = Image.Load<Rgb24>(Save(document, new JpegXlEncoder()));
        Assert.Equal(new Rgb24(120, 255, 7), image.Frames[0][1, 2]);
        Assert.Equal(new Rgb24(100, 200, 3), image.Frames[0][0, 0]);
    }

    [Fact]
    public void ASingleFrameFileHasOneLayer()
    {
        using var source = CreateImage(PixelFormat.Rgba32, 33, 17, seed: 3);
        var data = Save(source, new JpegXlEncoder());

        using var document = JpegXlLayeredImage.Load(data);
        var layer = Assert.Single(document.Layers);
        Assert.Equal((default(Point), JpegXlBlendMode.Replace, (string?)null), (layer.Offset, layer.BlendMode, layer.Name));
        Assert.Equal(GetBytes(source), GetBytes(layer.Image));
    }

    [Fact]
    public void AnAnimationHasNoLayers()
    {
        using var image = new Image<Rgba32>(4, 4);
        using var next = new Image<Rgba32>(4, 4, new Rgba32(1, 2, 3, 4));
        image.AppendFrame(next.Frames[0]);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 10);
        var data = Save(image, new JpegXlEncoder());

        Assert.Throws<UnsupportedImageFeatureException>(() => JpegXlLayeredImage.Load(data));
    }

    [Fact]
    public void LossyLayersKeepTheirGeometryAndAlpha()
    {
        using var document = new JpegXlLayeredImage(120, 80);
        AddLayer(document, PixelFormat.Rgba32, 120, 80, seed: 1, default, JpegXlBlendMode.Replace, "Background");
        AddLayer(document, PixelFormat.Rgba32, 45, 33, seed: 2, new Point(-7, 50), JpegXlBlendMode.Blend, "Overlay");
        var data = Save(document, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 95 });

        using var decoded = JpegXlLayeredImage.Load(data);
        Assert.Equal(2, decoded.Layers.Count);
        for (var i = 0; i < 2; i++)
        {
            var (expected, actual) = (document.Layers[i], decoded.Layers[i]);
            Assert.Equal((expected.Offset, expected.BlendMode, expected.Name, expected.Image.Size), (actual.Offset, actual.BlendMode, actual.Name, actual.Image.Size));

            // A layer is coded like the same image alone: it decodes to the same samples, with an exact alpha channel
            var encoder = new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 95 };
            using var alone = Image.Load(Save(expected.Image, encoder));
            Assert.True(GetBytes(alone).AsSpan().SequenceEqual(GetBytes(actual.Image)), $"Layer {i} decodes differently from the same image alone.");
            var source = GetBytes(expected.Image);
            var samples = GetBytes(actual.Image);
            for (var j = 3; j < source.Length; j += 4)
            {
                Assert.Equal(source[j], samples[j]);
            }
        }
    }

    [Fact]
    public void TheMetadataOfTheImageAreWrittenOnce()
    {
        // Display P3: an enumerated color encoding
        var cicp = new CicpColorDescription(12, 13, 0, fullRange: true);
        using var document = new JpegXlLayeredImage(10, 10);
        document.Metadata.Cicp = cicp;
        document.Metadata.Orientation = ExifOrientation.RightTop;
        using var layer = new Image<Rgb24>(10, 10, new Rgb24(1, 2, 3));
        document.AddLayer(layer);
        layer.Metadata.Cicp = cicp;
        document.AddLayer(layer, new Point(3, 3));
        var data = Save(document, new JpegXlEncoder());

        using var decoded = JpegXlLayeredImage.Load(data);
        Assert.Equal(cicp, decoded.Metadata.Cicp);
        Assert.Equal(ExifOrientation.RightTop, decoded.Metadata.Orientation);
        Assert.All(decoded.Layers, layer => Assert.Equal(cicp, layer.Image.Metadata.Cicp));
    }

    [Fact]
    public void LayersInAnotherColorSpaceAreRejected()
    {
        using var document = new JpegXlLayeredImage(4, 4);
        using var layer = new Image<Rgb24>(4, 4);
        layer.Metadata.TransferFunction = ColorTransferFunction.Linear;
        document.AddLayer(layer);

        Assert.Throws<UnsupportedImageFeatureException>(() => Save(document, new JpegXlEncoder()));
        document.Metadata.TransferFunction = ColorTransferFunction.Linear;
        using var decoded = JpegXlLayeredImage.Load(Save(document, new JpegXlEncoder()));
        Assert.Equal(ColorTransferFunction.Linear, decoded.Metadata.TransferFunction);
    }

    [Fact]
    public void LayersAreWidenedToAFormatTheEncoderStores()
    {
        using var document = new JpegXlLayeredImage(5, 3);
        using var layer = new Image<La16>(5, 3, new La16(40, 200));
        document.AddLayer(layer);
        document.AddLayer(layer, new Point(2, 1), JpegXlBlendMode.Blend);

        using var decoded = JpegXlLayeredImage.Load(Save(document, new JpegXlEncoder()));
        Assert.Equal(2, decoded.Layers.Count);
        using var expected = layer.CloneAs<Rgba32>();
        Assert.All(decoded.Layers, decodedLayer =>
        {
            using var converted = decodedLayer.Image.CloneAs<Rgba32>();
            Assert.Equal(GetBytes(expected), GetBytes(converted));
        });
    }

    [Fact]
    public void FloatingPointLayersNeedAConversion()
    {
        using var document = new JpegXlLayeredImage(3, 3);
        using var layer = new Image<RgbaFloat>(3, 3, new RgbaFloat(0.5f, 0.25f, 1, 1));
        document.AddLayer(layer);
        document.Metadata.TransferFunction = layer.Metadata.TransferFunction;

        Assert.Throws<UnsupportedImageFeatureException>(() => Save(document, new JpegXlEncoder()));
        using var decoded = JpegXlLayeredImage.Load(Save(document, new JpegXlEncoder { FloatingPointConversion = new PixelConversionOptions() }));
        Assert.Equal(PixelFormat.Rgba64, decoded.Layers[0].Image.PixelFormat);
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlLayeredImage(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegXlLayeredImage(1, (1 << 30) + 1));
        using var document = new JpegXlLayeredImage(4, 4);
        using var image = new Image<Rgba32>(2, 2);
        Assert.Throws<ArgumentNullException>(() => document.AddLayer(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.AddLayer(image, new Point(JpegXlLayer.MinOffset - 1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.AddLayer(image, new Point(0, JpegXlLayer.MaxOffset + 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.AddLayer(image, blendMode: (JpegXlBlendMode)5));
        Assert.Throws<ArgumentException>(() => document.AddLayer(image, name: new string('x', JpegXlLayer.MaxNameLength + 1)));
        Assert.Throws<ArgumentException>(() => document.AddLayer(image, name: "\uD800"));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.InsertLayer(1, image));
        Assert.Empty(document.Layers);

        // The longest name, the extreme offsets
        var layer = document.AddLayer(image, new Point(JpegXlLayer.MinOffset, JpegXlLayer.MaxOffset), name: new string('é', JpegXlLayer.MaxNameLength / 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => layer.BlendMode = (JpegXlBlendMode)(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layer.Offset = new Point(0, JpegXlLayer.MinOffset - 1));
        Assert.Throws<ArgumentException>(() => layer.Name = new string('é', (JpegXlLayer.MaxNameLength / 2) + 1));
        layer.Name = string.Empty;
        Assert.Null(layer.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => document.RemoveLayerAt(1));

        using var animation = new Image<Rgba32>(2, 2);
        animation.AppendFrame(image.Frames[0]);
        Assert.Throws<ArgumentException>(() => document.AddLayer(animation));
    }

    [Fact]
    public void ExtremeOffsetsAndLongNamesRoundTrip()
    {
        using var document = new JpegXlLayeredImage(4, 4);
        using var image = new Image<Gray8>(2, 2, new Gray8(9));
        document.AddLayer(image, new Point(JpegXlLayer.MinOffset, JpegXlLayer.MaxOffset), name: new string('é', JpegXlLayer.MaxNameLength / 2));
        document.AddLayer(image, new Point(JpegXlLayer.MaxOffset, JpegXlLayer.MinOffset), JpegXlBlendMode.Add, new string('a', JpegXlLayer.MaxNameLength));

        using var decoded = JpegXlLayeredImage.Load(Save(document, new JpegXlEncoder()));
        AssertSameLayers(document, decoded);
    }

    [Fact]
    public void LayersAreOwnedByTheLayeredImage()
    {
        using var source = new Image<Rgba32>(2, 2);
        var document = new JpegXlLayeredImage(2, 2);
        var first = document.AddLayer(source);
        var second = document.InsertLayer(0, source, name: "Bottom");
        Assert.NotSame(source, first.Image);
        Assert.Equal([second, first], document.Layers);

        document.RemoveLayerAt(1);
        Assert.Throws<ObjectDisposedException>(() => first.Image.Size);
        Assert.Equal([second], document.Layers);

        document.Dispose();
        Assert.Throws<ObjectDisposedException>(() => second.Image.Size);
        Assert.Throws<ObjectDisposedException>(() => document.AddLayer(source));
        Assert.Throws<ObjectDisposedException>(() => document.Save(new MemoryStream()));
        _ = source.Size;
    }

    [Fact]
    public void FramesAddedToALayerAreNotDroppedSilently()
    {
        using var document = new JpegXlLayeredImage(4, 4);
        using var image = new Image<Rgba32>(4, 4);
        var layer = document.AddLayer(image);
        layer.Image.AppendFrame(image.Frames[0]);
        Assert.Throws<InvalidOperationException>(() => Save(document, new JpegXlEncoder()));

        document.RemoveLayerAt(0);
        layer = document.AddLayer(image);
        layer.Image.SetPosterFrame(image.Frames[0]);
        Assert.Throws<InvalidOperationException>(() => Save(document, new JpegXlEncoder()));
    }

    [Fact]
    public void ALayerWhoseNameIsNotUtf8IsRejected()
    {
        using var document = new JpegXlLayeredImage(4, 4);
        using var image = new Image<Gray8>(4, 4, new Gray8(7));
        document.AddLayer(image, name: "NAMEXYZW");
        document.AddLayer(image, new Point(1, 1), JpegXlBlendMode.Add);
        var data = Save(document, new JpegXlEncoder());

        // The name is a run of 8-bit fields of the frame header, at any bit position (least significant bit first): its
        // first byte becomes 0xFF, which UTF-8 never uses
        var name = "NAMEXYZW"u8.ToArray();
        var (index, shift) = FindBits(data, name);
        data[index] |= (byte)(0xFF << shift);
        if (shift != 0)
        {
            data[index + 1] |= (byte)(0xFF >> (8 - shift));
        }

        var exception = Assert.Throws<InvalidImageContentException>(() => JpegXlLayeredImage.Load(data));
        Assert.Contains("the name of a frame is not UTF-8 text", exception.Message, StringComparison.Ordinal);

        static (int Index, int Shift) FindBits(byte[] data, byte[] value)
        {
            for (var shift = 0; shift < 8; shift++)
            {
                for (var i = 0; i + value.Length < data.Length; i++)
                {
                    var match = true;
                    for (var j = 0; j < value.Length && match; j++)
                    {
                        match = (byte)((data[i + j] >> shift) | (data[i + j + 1] << (8 - shift))) == value[j];
                    }

                    if (match)
                        return (i, shift);
                }
            }

            throw new InvalidOperationException("The name is not in the file.");
        }
    }

    /// <summary>Each layer counts as a frame of its own size, charged before it is decoded.</summary>
    [Fact]
    public void LayersCountTowardTheFrameAndPixelLimits()
    {
        using var document = new JpegXlLayeredImage(20, 10);
        AddLayer(document, PixelFormat.Rgba32, 20, 10, seed: 1, default, JpegXlBlendMode.Replace, null);
        AddLayer(document, PixelFormat.Rgba32, 30, 7, seed: 2, new Point(-3, 2), JpegXlBlendMode.Blend, null);
        var data = Save(document, new JpegXlEncoder());
        const long Pixels = (20 * 10) + (30 * 7);

        using (JpegXlLayeredImage.Load(data, new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 2, MaxTotalPixels = Pixels } }))
        {
        }

        Assert.Throws<ImageResourceLimitException>(() => JpegXlLayeredImage.Load(data, new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 1 } }));
        Assert.Throws<ImageResourceLimitException>(() => JpegXlLayeredImage.Load(data, new ImageConfiguration { Limits = new ImageResourceLimits { MaxTotalPixels = Pixels - 1 } }));
    }

    [Fact]
    public void TheHotspotOfALayerFollowsTheMetadataPolicy()
    {
        using var document = new JpegXlLayeredImage(4, 4);
        using var image = new Image<Rgba32>(4, 4);
        var layer = document.AddLayer(image);
        layer.Image.Frames[0].Metadata.Hotspot = new Point(1, 2);

        Assert.Throws<UnsupportedImageFeatureException>(() => Save(document, new JpegXlEncoder()));
        using var decoded = JpegXlLayeredImage.Load(Save(document, new JpegXlEncoder { MetadataHandling = MetadataHandling.Strip }));
        Assert.Single(decoded.Layers);
    }

    /// <summary>
    /// A layer converted for the encoder is a copy of the save, which counts toward the allocation limit of the layered
    /// image: a gray layer with alpha, widened to RGBA, needs at least its 360,000-byte RGBA copy more than the same pixels
    /// given as RGBA.
    /// </summary>
    [Fact]
    public void ConvertedLayersCountTowardTheAllocationLimit()
    {
        using var gray = new Image<La16>(300, 300, new La16(40, 200));
        using var rgba = gray.CloneAs<Rgba32>();
        Assert.True(GetSmallestLimit(gray) - GetSmallestLimit(rgba) >= 300 * 300 * 4);

        static long GetSmallestLimit(Image layer)
        {
            var (low, high) = (1L, 64L << 20);
            while (low < high)
            {
                var limit = (low + high) / 2;
                using var document = new JpegXlLayeredImage(300, 300, new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = limit } });
                document.AddLayer(layer);
                try
                {
                    Save(document, new JpegXlEncoder { Effort = 0 });
                    high = limit;
                }
                catch (ImageResourceLimitException)
                {
                    low = limit + 1;
                }
            }

            return low;
        }
    }

    [Fact]
    public void AnImageWithoutLayersCannotBeSaved()
    {
        using var document = new JpegXlLayeredImage(4, 4);
        Assert.Throws<InvalidOperationException>(() => Save(document, new JpegXlEncoder()));
    }

    [Fact]
    public void LayersOfDifferentPixelFormatsAreRejected()
    {
        using var document = new JpegXlLayeredImage(4, 4);
        using var first = new Image<Rgba32>(4, 4);
        using var second = new Image<Rgb24>(4, 4);
        document.AddLayer(first);
        document.AddLayer(second);
        Assert.Throws<InvalidOperationException>(() => Save(document, new JpegXlEncoder()));
    }

    [Fact]
    public void LayersAreWrittenToNonSeekableStreamsAndToPaths()
    {
        using var document = new JpegXlLayeredImage(20, 20);
        AddLayer(document, PixelFormat.Rgba32, 20, 20, seed: 8, default, JpegXlBlendMode.Replace, null);
        AddLayer(document, PixelFormat.Rgba32, 5, 5, seed: 9, new Point(3, 3), JpegXlBlendMode.Blend, "Top");
        using var stream = new TestOutputStream();
        document.Save(stream);
        Assert.Equal(Save(document, new JpegXlEncoder()), stream.ToArray());

        var path = Path.Combine(Path.GetTempPath(), $"layers-{Guid.NewGuid():N}.jxl");
        try
        {
            document.Save(path);
            using var decoded = JpegXlLayeredImage.Load(path);
            AssertSameLayers(document, decoded);
            using var file = File.OpenRead(path);
            using var fromStream = JpegXlLayeredImage.Load(file);
            AssertSameLayers(document, fromStream);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32, false)]
    [InlineData(PixelFormat.Rgba32, true)]
    [InlineData(PixelFormat.Rgb24, false)]
    [InlineData(PixelFormat.Gray16, false)]
    [InlineData(PixelFormat.Rgba64, true)]
    public void OptimizedAnimationsRoundTripExactlyAndAreSmaller(PixelFormat format, bool progressive)
    {
        using var image = CreateMovingAnimation(format, 300, 280, frameCount: 5);
        var full = Save(image, new JpegXlEncoder { Progressive = progressive });
        var optimized = Save(image, new JpegXlEncoder { Progressive = progressive, OptimizeFrames = true });
        Assert.HasCountGreaterThan(optimized.Length * 2, full, $"The optimized animation has {optimized.Length} bytes, the full one {full.Length}.");

        using var decoded = Image.Load(optimized);
        Assert.Equal(image.Frames.Count, decoded.Frames.Count);
        for (var i = 0; i < image.Frames.Count; i++)
        {
            Assert.Equal(0, image.Frames[i].Metadata.Duration.CompareTo(decoded.Frames[i].Metadata.Duration));
            Assert.True(GetFrameBytes(image, i).AsSpan().SequenceEqual(GetFrameBytes(decoded, i)), $"The samples of frame {i} differ.");
        }
    }

    /// <summary>The frames of a lossy animation written as the regions that change are as close to the source as full frames, and the alpha channel stays exact.</summary>
    [Fact]
    public void OptimizedLossyAnimationsAreAsCloseAsFullFrames()
    {
        using var image = CreateMovingAnimation(PixelFormat.Rgba32, 160, 120, frameCount: 4);
        var full = Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 95 });
        var optimized = Save(image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = 95, OptimizeFrames = true });
        Assert.HasCountLessThan(full.Length, optimized, $"The optimized animation has {optimized.Length} bytes, the full one {full.Length}.");

        using var decodedFull = Image.Load<Rgba32>(full);
        using var decoded = Image.Load<Rgba32>(optimized);
        Assert.Equal(image.Frames.Count, decoded.Frames.Count);
        for (var i = 0; i < image.Frames.Count; i++)
        {
            var expected = GetFrameBytes(image, i);
            var actual = GetFrameBytes(decoded, i);
            var reference = GetFrameBytes(decodedFull, i);
            var (error, referenceError) = (0L, 0L);
            for (var j = 0; j < expected.Length; j++)
            {
                if (j % 4 == 3)
                {
                    Assert.Equal(expected[j], actual[j]);
                }

                error += Math.Abs(expected[j] - actual[j]);
                referenceError += Math.Abs(expected[j] - reference[j]);
            }

            Assert.True(error <= referenceError * 1.05, $"Frame {i}: the mean error is {error / (double)expected.Length}, {referenceError / (double)expected.Length} with full frames.");
        }
    }

    [Fact]
    public void AnOptimizedFrameEqualToThePreviousOneIsASinglePixel()
    {
        using var image = new Image<Rgb24>(100, 100, new Rgb24(1, 2, 3));
        using var copy = image.Clone();
        for (var i = 0; i < 3; i++)
        {
            image.AppendFrame(copy.Frames[0]);
        }

        foreach (var frame in image.Frames)
        {
            frame.Metadata.Duration = new FrameDuration(1, 10);
        }

        var data = Save(image, new JpegXlEncoder { OptimizeFrames = true });
        using var decoded = Image.Load<Rgb24>(data);
        Assert.Equal(4, decoded.Frames.Count);
        for (var i = 0; i < decoded.Frames.Count; i++)
        {
            Assert.Equal(new Rgb24(1, 2, 3), decoded.Frames[i][99, 99]);
        }
    }

    private static void AddLayer(JpegXlLayeredImage document, PixelFormat format, int width, int height, int seed, Point offset, JpegXlBlendMode mode, string? name)
    {
        using var image = CreateImage(format, width, height, seed);
        document.AddLayer(image, offset, mode, name);
    }

    private static void AssertSameLayers(JpegXlLayeredImage expected, JpegXlLayeredImage actual)
    {
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.Layers.Count, actual.Layers.Count);
        for (var i = 0; i < expected.Layers.Count; i++)
        {
            var (source, decoded) = (expected.Layers[i], actual.Layers[i]);
            Assert.Equal((source.Offset, source.BlendMode, source.Name, source.Image.Size), (decoded.Offset, decoded.BlendMode, decoded.Name, decoded.Image.Size));
            Assert.Equal(PixelFormatOf(source.Image.PixelFormat), decoded.Image.PixelFormat);
            Assert.True(GetBytes(source.Image).AsSpan().SequenceEqual(GetBytes(decoded.Image)), $"The samples of layer {i} differ.");
        }

        // Bgra32 is written as RGBA
        static PixelFormat PixelFormatOf(PixelFormat format) => format == PixelFormat.Bgra32 ? PixelFormat.Rgba32 : format;
    }

    /// <summary>An animation of a background with a square that moves, and a band whose color changes.</summary>
    private static Image CreateMovingAnimation(PixelFormat format, int width, int height, int frameCount)
    {
        using var background = CreateImage(PixelFormat.Rgba32, width, height, seed: 42);
        var rgba = (Image<Rgba32>)background.Clone();
        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                using var frame = (Image<Rgba32>)background.Clone();
                for (var y = 0; y < 20; y++)
                {
                    for (var x = 0; x < 20; x++)
                    {
                        frame.Frames[0][(i * 13) + x + 10, (i * 7) + y + 30] = new Rgba32((byte)(i * 40), 255, (byte)(y * 9), 255);
                    }
                }

                if (i == 0)
                {
                    rgba.Dispose();
                    rgba = (Image<Rgba32>)frame.Clone();
                }
                else
                {
                    rgba.AppendFrame(frame.Frames[0]);
                }
            }

            foreach (var frame in rgba.Frames)
            {
                frame.Metadata.Duration = new FrameDuration(1, 20);
            }

            var flatten = new PixelConversionOptions { BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) };
            return format == PixelFormat.Rgba32 ? rgba : rgba.CloneAs(format, flatten);
        }
        finally
        {
            if (format != PixelFormat.Rgba32)
            {
                rgba.Dispose();
            }
        }
    }

    /// <summary>Flat areas, gradients, noise, translucent and fully transparent pixels with defined colors, in a pixel format (flattened on white without alpha).</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image CreateImage(PixelFormat format, int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (((x / 3) + (y / 40)) % 4) switch
                {
                    0 => new Rgba32((byte)(30 + seed), 60, 90, 255),
                    1 => new Rgba32((byte)(x * 5), (byte)(y * 7), (byte)(x + y), 255),
                    2 => new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
                    _ => new Rgba32((byte)random.Next(256), (byte)x, (byte)y, 0),
                };
            }
        }

        using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
        var flatten = new PixelConversionOptions { BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) };
        return format switch
        {
            PixelFormat.Rgba32 => image.Clone(),
            PixelFormat.Bgra32 => image.CloneAs<Bgra32>(),
            PixelFormat.Rgba64 => image.CloneAs<Rgba64>(),
            PixelFormat.Rgb24 => image.CloneAs<Rgb24>(flatten),
            PixelFormat.Gray8 => image.CloneAs<Gray8>(flatten),
            PixelFormat.Gray16 => image.CloneAs<Gray16>(flatten),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static byte[] Save(JpegXlLayeredImage image, JpegXlEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Gets the samples of the first frame, as RGBA for the 8-bit color formats (Rgba32 and Bgra32 are the same samples in another order).</summary>
    private static byte[] GetBytes(Image image) => GetFrameBytes(image, 0);

    private static byte[] GetFrameBytes(Image image, int frame)
    {
        if (image.PixelFormat == PixelFormat.Bgra32)
        {
            using var converted = image.CloneAs<Rgba32>();
            return GetFrameBytes(converted, frame);
        }

        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[frame].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
