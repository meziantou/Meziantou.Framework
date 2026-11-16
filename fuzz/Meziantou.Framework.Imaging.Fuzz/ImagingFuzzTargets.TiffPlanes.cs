using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The separate planes of the TIFF encoder (<see cref="TiffEncoder.SeparatePlanes"/>). The input is a byte of settings
/// (compression, predictor, tiles, byte order, offset size, layout), a byte of rows per strip, then an image (its first
/// frame is the page). The page written in separate planes and the one written with chunky samples succeed or fail
/// together, and decode to the same pixels; an uncompressed page written to a stream that cannot seek is the file written
/// to a seekable one.
/// </summary>
public static partial class ImagingFuzzTargets
{
    private static readonly TiffCompression[] TiffPlaneCompressions = [TiffCompression.None, TiffCompression.Deflate, TiffCompression.Lzw, TiffCompression.PackBits];
    private static readonly TiffPixelLayout[] TiffPlaneLayouts = [TiffPixelLayout.Auto, TiffPixelLayout.Auto, TiffPixelLayout.Indexed, TiffPixelLayout.Bilevel];

    private static void TiffPlanesTarget(ReadOnlySpan<byte> data)
    {
        var settings = FuzzInput.ReadByte(ref data);
        var rowsPerStrip = FuzzInput.ReadByte(ref data) % 16;
        var layout = TiffPlaneLayouts[settings >> 6];
        var compression = TiffPlaneCompressions[settings & 3];
        var tiled = (settings & 8) != 0;
        var chunky = new TiffEncoder
        {
            Compression = compression,

            // Horizontal differencing is accepted with Deflate and LZW and the layout that follows the pixel format
            Predictor = (settings & 4) != 0 && compression is TiffCompression.Deflate or TiffCompression.Lzw && layout == TiffPixelLayout.Auto ? TiffPredictor.HorizontalDifferencing : TiffPredictor.None,
            TileWidth = tiled ? 16 : 0,
            TileHeight = tiled ? 16 : 0,
            BigEndian = (settings & 0x10) != 0,
            BigTiff = (settings & 0x20) != 0,
            PixelLayout = layout,
            RowsPerStrip = rowsPerStrip,
            MetadataHandling = MetadataHandling.Strip,
        };

        var planar = new TiffEncoder
        {
            Compression = chunky.Compression,
            Predictor = chunky.Predictor,
            TileWidth = chunky.TileWidth,
            TileHeight = chunky.TileHeight,
            BigEndian = chunky.BigEndian,
            BigTiff = chunky.BigTiff,
            PixelLayout = chunky.PixelLayout,
            RowsPerStrip = chunky.RowsPerStrip,
            MetadataHandling = chunky.MetadataHandling,
            SeparatePlanes = true,
        };

        using var frames = CreateImage(ref data, maxSide: 40);
        using var image = frames.CloneFrame(0);
        if (PixelFormats.IsFloatingPoint(image.PixelFormat))
        {
            // A floating-point page without ICC profile is linear light
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }

        byte[]? chunkyFile = null;
        byte[]? planarFile = null;
        var chunkyResult = Catch(() => chunkyFile = Save(image, chunky));
        var planarResult = Catch(() => planarFile = Save(image, planar));
        FuzzAssert.Equal(chunkyResult.Exception?.GetType(), planarResult.Exception?.GetType(), "The chunky and the planar encodings fail differently");
        if (chunkyFile is null || planarFile is null)
            return;

        using (var expected = Image.Load(chunkyFile))
        using (var actual = Image.Load(planarFile))
        {
            FuzzAssert.Equal(expected.PixelFormat, actual.PixelFormat, "The pixel format of the planar page");
            FuzzAssert.Equal(expected.Size, actual.Size, "The size of the planar page");
            FuzzAssert.True(GetPixelBytes(expected).AsSpan().SequenceEqual(GetPixelBytes(actual)), "The planar page decodes to other pixels than the chunky page");
        }

        FuzzAssert.True(Save(image, planar).AsSpan().SequenceEqual(planarFile), "The planar encoding is not deterministic");
        if (compression == TiffCompression.None)
        {
            using var forwardOnly = new ForwardOnlyStream();
            image.Save(forwardOnly, planar);
            FuzzAssert.True(forwardOnly.ToArray().AsSpan().SequenceEqual(planarFile), "An uncompressed page written forward only is not the file written to a seekable stream");
        }
    }

    private static IEnumerable<FuzzSeed> GetTiffPlanesSeeds()
    {
        // The cases of the unit tests: RGBA in uncompressed strips of 5 rows, RGB in LZW with the predictor, 16-bit RGB in
        // Deflate tiles with the predictor, floating-point RGBA (Deflate, its predictor), big-endian PackBits tiles,
        // a BigTIFF, the palette and bilevel layouts, and a gray page of one sample
        yield return new FuzzSeed("rgba-none-strips", (byte[])[0x00, 5, 0, 37, 23, 0, 200, 30, 90, 255, 3, 5, 8, 21, 77, 1, 2, 250]);
        yield return new FuzzSeed("rgb-lzw-predictor", (byte[])[0x06, 0, 1, 2, 1, 0, 1, 2, 3, 5, 1, 250]);
        yield return new FuzzSeed("rgb48-deflate-tiles-predictor", (byte[])[0x0D, 0, 11, 20, 17, 0, 209, 3, 255, 255, 0, 0, 31, 0, 7, 1]);
        yield return new FuzzSeed("float-deflate", (byte[])[0x01, 0, 6, 9, 4, 0, 0, 0, 128, 63, 0, 0, 192, 127, 0, 0, 128, 255, 1, 0, 0, 0]);
        yield return new FuzzSeed("big-endian-packbits-tiles", (byte[])[0x1B, 0, 4, 21, 18, 0, 7, 13, 255, 0, 9, 9, 9, 9]);
        yield return new FuzzSeed("bigtiff-lzw-rgba64", (byte[])[0x22, 7, 4, 30, 9, 0, 1, 2, 3, 4, 255, 255, 0, 100]);
        yield return new FuzzSeed("indexed-lzw", (byte[])[0x82, 3, 1, 33, 21, 0, 32, 64, 96, 0, 0, 0, 224, 192, 224]);
        yield return new FuzzSeed("bilevel-none-tiles", (byte[])[0xC8, 0, 2, 40, 17, 0, 0, 255, 255, 0, 255]);
        yield return new FuzzSeed("gray-deflate", (byte[])[0x01, 4, 2, 37, 23, 0, 10, 12, 9, 200, 100, 0]);
    }

    /// <summary>A destination that cannot seek: an uncompressed one-page document is written to it forward only.</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _data = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => _data.ToArray();

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _data.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => _data.Write(buffer);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _data.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
