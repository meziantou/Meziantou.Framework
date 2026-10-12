using System.Buffers.Binary;
using System.Numerics;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Avif;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The reference of the images that are built from other images (ISO/IEC 23008-12, section 6.6): overlays, grids whose
// tiles are derived images, and the identity derivations of those. Each coded image is decoded by the two reference
// decoders and converted like an image that is presented alone; the derivations are then evaluated on pixels, with
// exact fractions for the compositing. Nothing here is shared with the library.
internal static partial class AvifCorpus
{
    /// <summary>Returns whether the item is built from other images: its tree has an overlay, or a grid whose tiles are not all coded items.</summary>
    private static bool IsBuiltImage(ReferenceAvif file, ReferenceAvif.Item item)
    {
        var leaf = file.GetIdentityChain(item)[^1];
        return leaf.Type == "iovl" || (leaf.Type == "grid" && file.GetGridTiles(leaf).Any(tile => tile.Type != "av01"));
    }

    private static IReadOnlyList<ReferenceAvif.Item> GetInputs(ReferenceAvif file, ReferenceAvif.Item item)
    {
        var references = file.ItemReferences.Where(reference => reference.Type == "dimg" && reference.FromItemId == item.Id).ToList();
        Py.Assert(references.Count == 1, "a derived image has one list of input images");
        return [.. references[0].ToItemIds.Select(id => file.FindItem(id) ?? throw new ToolException("an input image does not exist."))];
    }

    /// <summary>The coded images of the tree of an item, in the order they are rendered (an item that is used twice is listed twice).</summary>
    private static List<ReferenceAvif.Item> CodedImages(ReferenceAvif file, ReferenceAvif.Item item)
    {
        if (!IsBuiltImage(file, item) && item.Type != "iden")
            return [item];

        return [.. GetInputs(file, item).SelectMany(input => CodedImages(file, input))];
    }

    private static AvifReference DescribeBuiltStill(AvifFileTools tools, FullPath scratch, ReferenceAvif file, SortedSet<string> features)
    {
        var primary = file.PrimaryItem!;
        var coded = CodedImages(file, primary);
        var depths = coded.SelectMany(item => new[] { item, file.FindAuxiliary(item.Id, ReferenceAvif.AlphaAuxiliaryType) }).Where(item => item is not null)
            .Select(item => (item!.Type == "grid" ? file.GetGridTiles(item)[0] : item).Configuration!.BitDepth).ToList();
        var deep = depths.Max() > 8;
        var payloads = new List<DecodedPayload>();
        var isMatrixed = false;
        ReferenceAvif.ColorDescription? bitstreamDescription = null;
        long inputPixels = 0;
        var counter = 0;
        var image = Render(primary, isRoot: true, depth: 0);

        // The transformations of the primary item: the clean aperture selects a part of the image that is built, and the
        // rotation and the mirroring are its orientation
        var crop = ReferenceAvif.Item.Present(primary.Transforms, image.Width, image.Height)?.Rectangle ?? throw new ToolException("the clean aperture is not a whole number of samples.");
        var pixels = new List<Px>();
        for (var y = crop.Y; y < crop.Y + crop.Height; y++)
        {
            for (var x = crop.X; x < crop.X + crop.Width; x++)
                pixels.Add(image.Pixels[(y * image.Width) + x]);
        }

        features.Add("avif.alpha");
        features.Add(Inv($"avif.derived.inputPixels={inputPixels}"));
        if (primary.CleanAperture is not null)
            features.Add("avif.clap");
        foreach (var transform in primary.Transforms.Where(t => t.Type is "irot" or "imir"))
            features.Add(Inv($"avif.{transform.Type}={transform.Payload.Span[0] & 3}"));
        if (deep)
            features.Add(Inv($"avif.bitDepth={depths.Max()}"));

        // The color primaries and the transfer characteristics of the image are those of its own colr box, or else of
        // its input images; its pixels are red, green and blue samples in full range
        // (the first image of its tree that has one), or else of the bitstream of its coded images; every description is
        // the same one, since nothing converts the colors of an input image
        var tree = file.GetImageTree(primary);
        var nclx = tree.Select(item => item.Nclx).FirstOrDefault(value => value is not null) ?? bitstreamDescription;
        Py.Assert(tree.Select(item => item.Nclx).Append(bitstreamDescription).All(value => value is null || nclx is null || (value.ColorPrimaries, value.TransferCharacteristics) == (nclx.ColorPrimaries, nclx.TransferCharacteristics)), "the images have the color space of the image");
        var icc = tree.Select(item => item.IccProfile).FirstOrDefault(value => value is not null);
        if (tree.All(item => item.Nclx is null))
            features.Add(icc is null ? "avif.colr=none" : "avif.colr=icc");
        if (primary.IccProfile is null && icc is not null)
            features.Add("avif.derived.colr=icc");
        Py.Assert(tree.All(item => item.IccProfile is not { } profile || profile.Span.SequenceEqual(icc!.Value.Span)), "the images have the ICC profile of the image");
        var descriptions = file.FindDescriptions(primary.Id);
        var exifItem = descriptions.FirstOrDefault(item => item.Type == "Exif");
        var xmpItem = descriptions.FirstOrDefault(item => item.ContentType == "application/rdf+xml");
        return new AvifReference
        {
            Frames = [new Img(crop.Width, crop.Height, "rgba", deep ? 16 : 8, pixels)],
            PixelFormat = deep ? "Rgba64" : "Rgba32",
            ColorModel = "Rgba",
            BitsPerComponent = depths.Max(),
            Orientation = primary.Orientation,
            Cicp = nclx is null ? null : new ReferenceAvif.ColorDescription(nclx.ColorPrimaries, nclx.TransferCharacteristics, 0, FullRange: true),
            Icc = icc?.ToArray(),
            Exif = exifItem is null ? null : file.ReadExifPayload(exifItem),
            Xmp = xmpItem is null ? null : file.ReadItemData(xmpItem),
            IsMatrixed = isMatrixed,
            Timings = [],
            IsSequence = false,
            ColorTrack = null,
            Features = features,
            HiddenFrames = payloads.Sum(payload => payload.HiddenFrames),
            LowerLayerPixels = payloads.Sum(payload => payload.LowerLayerPixels),
        };

        // What the item reconstructs, then its own transformations (those of the primary item are applied by the caller)
        Img Render(ReferenceAvif.Item item, bool isRoot, int depth)
        {
            Py.Assert(depth <= 8, "the derivations are nested at most eight deep");
            Img result;
            if (item.Type == "iden")
            {
                features.Add("avif.derived.iden");
                result = Render(GetInputs(file, item).Single(), isRoot: false, depth + 1);
            }
            else if (item.Type == "iovl")
            {
                features.Add("avif.iovl");
                result = Overlay(item, depth);
            }
            else if (item.Type == "grid" && file.GetGridTiles(item).Any(tile => tile.Type != "av01"))
            {
                features.Add("avif.derived.grid");
                result = Tiles(item, depth);
            }
            else
            {
                result = Coded(item);
            }

            Py.Assert(item.Size == (result.Width, result.Height), "the ispe property of an item is the size of the image it reconstructs");
            return isRoot ? result : Transform(result, item.Transforms);
        }

        Img Coded(ReferenceAvif.Item item)
        {
            var label = Inv($"input-{counter++}");
            var (color, colorPayloads, _, _) = DecodeItem(tools, scratch, file, item, label + "-color");
            payloads.AddRange(colorPayloads);
            PlaneImage? alpha = null;
            if (file.FindAuxiliary(item.Id, ReferenceAvif.AlphaAuxiliaryType) is { } alphaItem)
            {
                (alpha, var alphaPayloads, _, _) = DecodeItem(tools, scratch, file, alphaItem, label + "-alpha");
                payloads.AddRange(alphaPayloads);
            }

            inputPixels += (long)color.Width * color.Height * (alpha is null ? 1 : 2);
            var (matrix, fullRange, cicp) = ResolveColor(item.Nclx, color);
            isMatrixed |= !color.Color.Monochrome && matrix != 0;
            if (!color.Color.Monochrome && matrix != 0)
                features.Add(Inv($"avif.derived.matrix={matrix}"));
            if (item.Nclx is null && cicp is not null)
            {
                Py.Assert(bitstreamDescription is null || (bitstreamDescription.ColorPrimaries, bitstreamDescription.TransferCharacteristics) == (cicp.ColorPrimaries, cicp.TransferCharacteristics), "the bitstreams have the same color space");
                bitstreamDescription ??= cicp;
            }

            var isPremultiplied = alpha is not null && file.ItemReferences.Any(reference => reference.Type == "prem" && reference.FromItemId == item.Id);
            var frame = ToFrame(color, alpha, matrix, fullRange, (0, 0, color.Width, color.Height), isPremultiplied, cicp?.ColorPrimaries ?? 2, cicp?.TransferCharacteristics ?? 2);

            // Straight red, green, blue and alpha samples at the depth of the image: gray samples are equal colors, and an
            // 8-bit sample in a 16-bit image is v * 65535 / 255 = v * 257
            var maximum = deep ? 65535 : 255;
            var scale = deep && frame.Depth == 8 ? 257 : 1;
            return new Img(frame.Width, frame.Height, "rgba", deep ? 16 : 8, frame.Pixels.Select(pixel => frame.Model switch
            {
                "gray" => new Px(pixel[0] * scale, pixel[0] * scale, pixel[0] * scale, maximum),
                "graya" => new Px(pixel[0] * scale, pixel[0] * scale, pixel[0] * scale, pixel[1] * scale),
                "rgb" => new Px(pixel[0] * scale, pixel[1] * scale, pixel[2] * scale, maximum),
                _ => new Px(pixel[0] * scale, pixel[1] * scale, pixel[2] * scale, pixel[3] * scale),
            }));
        }

        // ISO/IEC 23008-12, section 6.6.2.3.2: the tiles side by side, row by row; the last row and column may exceed the image
        Img Tiles(ReferenceAvif.Item item, int depth)
        {
            var (rows, columns, width, height) = file.ReadGridDescriptor(item);
            var inputs = GetInputs(file, item);
            Py.Assert(inputs.Count == rows * columns, "one tile per cell");
            var tiles = inputs.Select(input => Render(input, isRoot: false, depth + 1)).ToList();
            var (tileWidth, tileHeight) = (tiles[0].Width, tiles[0].Height);
            Py.Assert(tiles.All(tile => (tile.Width, tile.Height) == (tileWidth, tileHeight)), "the tiles have the same size");
            var canvas = new Px[(int)(width * height)];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    canvas[(y * (int)width) + x] = tiles[((y / tileHeight) * columns) + (x / tileWidth)].Pixels[((y % tileHeight) * tileWidth) + (x % tileWidth)];
            }

            return new Img((int)width, (int)height, "rgba", deep ? 16 : 8, canvas);
        }

        // ISO/IEC 23008-12, section 6.6.2.3 "Image overlay": version, flags, the fill color (four 16-bit values: red, green,
        // blue, alpha), the size of the canvas and the offset of each input image, on 16 bits or on 32 bits (flags and 1).
        // The input images are composited in their order with the source-over operator on straight alpha, in exact
        // fractions: the alpha is as + ad (1 - as), each color (cs as + cd ad (1 - as)) over that alpha, rounded to the
        // nearest sample, halves upward
        Img Overlay(ReferenceAvif.Item item, int depth)
        {
            var data = file.ReadItemData(item);
            Py.Assert(data[0] == 0, "overlay version");
            var large = (data[1] & 1) != 0;
            var maximum = deep ? 65535 : 255;
            var fill = new int[4];
            for (var i = 0; i < 4; i++)
                fill[i] = (int)(((BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2 + (2 * i))) * (long)maximum * 2) + 65535) / (2 * 65535));

            // A canvas that is filled with a transparent color is transparent black: its colors would never show
            if (fill[3] == 0)
                fill = [0, 0, 0, 0];

            var position = 10;
            long Read(bool signed)
            {
                long value = large
                    ? (signed ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position)))
                    : (signed ? BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(position)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position)));
                position += large ? 4 : 2;
                return value;
            }

            var (width, height) = ((int)Read(signed: false), (int)Read(signed: false));
            var canvas = Enumerable.Repeat(new Px(fill[0], fill[1], fill[2], fill[3]), width * height).ToArray();
            foreach (var input in GetInputs(file, item))
            {
                var (left, top) = (Read(signed: true), Read(signed: true));
                var layer = Render(input, isRoot: false, depth + 1);
                for (var y = 0; y < layer.Height; y++)
                {
                    for (var x = 0; x < layer.Width; x++)
                    {
                        var (cx, cy) = (left + x, top + y);
                        if (cx < 0 || cy < 0 || cx >= width || cy >= height)
                            continue;

                        var index = (int)((cy * width) + cx);
                        canvas[index] = Over(layer.Pixels[(y * layer.Width) + x], canvas[index], maximum);
                    }
                }
            }

            Py.Assert(position == data.Length, "the overlay descriptor has an offset for each input image");
            return new Img(width, height, "rgba", deep ? 16 : 8, canvas);
        }

        static Px Over(Px source, Px destination, int maximum)
        {
            if (source[3] == 0)
                return destination;

            // In units of 1 / maximum^2: the alpha of the result, and the color of the result times that alpha
            BigInteger m = maximum;
            var alpha = (source[3] * m) + (destination[3] * (m - source[3]));
            var samples = new int[4];
            for (var i = 0; i < 3; i++)
            {
                var color = (source[i] * (BigInteger)source[3] * m) + (destination[i] * (BigInteger)destination[3] * (m - source[3]));
                samples[i] = (int)(((2 * color) + alpha) / (2 * alpha));
            }

            samples[3] = (int)(((2 * alpha) + m) / (2 * m));
            return new Px(samples[0], samples[1], samples[2], samples[3]);
        }

        // The transformative properties of an item, applied to pixels in the order of their association
        Img Transform(Img source, IReadOnlyList<ReferenceAvif.Property> transforms)
        {
            var transformed = source;
            foreach (var property in transforms)
            {
                var (w, h) = (transformed.Width, transformed.Height);
                var current = transformed;
                if (property.Type == "clap")
                {
                    var aperture = ReferenceAvif.CleanAperture.Parse(property.Payload.Span).GetRectangle(w, h) ?? throw new ToolException("the clean aperture is not a whole number of samples.");
                    transformed = new Img(aperture.Width, aperture.Height, "rgba", current.Depth, CommonGrid(aperture.Width, aperture.Height, (x, y) => current.Pixels[((aperture.Y + y) * w) + aperture.X + x]));
                }
                else if (property.Type == "irot")
                {
                    // Quarter turns anti-clockwise: one turn brings the right column to the top row
                    transformed = (property.Payload.Span[0] & 3) switch
                    {
                        1 => new Img(h, w, "rgba", current.Depth, CommonGrid(h, w, (x, y) => current.Pixels[(x * w) + w - 1 - y])),
                        2 => new Img(w, h, "rgba", current.Depth, CommonGrid(w, h, (x, y) => current.Pixels[((h - 1 - y) * w) + w - 1 - x])),
                        3 => new Img(h, w, "rgba", current.Depth, CommonGrid(h, w, (x, y) => current.Pixels[((h - 1 - x) * w) + y])),
                        _ => current,
                    };
                }
                else
                {
                    // imir: 0 exchanges the top and the bottom, 1 the left and the right
                    transformed = (property.Payload.Span[0] & 1) == 0
                        ? new Img(w, h, "rgba", current.Depth, CommonGrid(w, h, (x, y) => current.Pixels[((h - 1 - y) * w) + x]))
                        : new Img(w, h, "rgba", current.Depth, CommonGrid(w, h, (x, y) => current.Pixels[(y * w) + w - 1 - x]));
                }
            }

            return transformed;
        }

        static IEnumerable<Px> CommonGrid(int width, int height, Func<int, int, Px> at)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    yield return at(x, y);
            }
        }
    }
}
