namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes the color tables of a font from their description: OpenType specification, "CPAL - Color Palette Table" and
/// "COLR - Color Table".
/// </summary>
internal static class FontColorWriter
{
    /// <summary>Writes the <c>CPAL</c> table: palettes of the same number of colors.</summary>
    /// <param name="palettes">The colors of each palette: red, green, blue and alpha.</param>
    /// <param name="types">The backgrounds each palette is made for (1: light, 2: dark), which makes the table a version 1; <see langword="null"/> for a version 0.</param>
    public static byte[] WriteCpal(IReadOnlyList<(byte R, byte G, byte B, byte A)[]> palettes, IReadOnlyList<int>? types = null)
    {
        var entryCount = palettes[0].Length;
        if (palettes.Any(palette => palette.Length != entryCount))
            throw new InvalidOperationException("Every palette has the same number of colors");

        var headerLength = 12 + (palettes.Count * 2) + (types is null ? 0 : 12);
        var writer = new FontWriter();
        writer.UInt16(types is null ? 0 : 1);
        writer.UInt16(entryCount);
        writer.UInt16(palettes.Count);
        writer.UInt16(palettes.Count * entryCount);
        writer.UInt32((uint)headerLength);
        for (var i = 0; i < palettes.Count; i++)
        {
            writer.UInt16(i * entryCount);
        }

        if (types is not null)
        {
            // The types of the palettes follow the colors; the palettes and their entries have no label.
            writer.UInt32((uint)(headerLength + (palettes.Count * entryCount * 4)));
            writer.UInt32(0);
            writer.UInt32(0);
        }

        foreach (var (r, g, b, a) in palettes.SelectMany(palette => palette))
        {
            writer.Byte(b);
            writer.Byte(g);
            writer.Byte(r);
            writer.Byte(a);
        }

        foreach (var type in types ?? [])
        {
            writer.UInt32((uint)type);
        }

        return writer.ToArray();
    }

    /// <summary>Writes the <c>COLR</c> table.</summary>
    /// <param name="layers">Version 0: the layers of each glyph, from the bottom to the top, as a glyph and an entry of the palette.</param>
    /// <param name="paints">Version 1: the paint of each glyph; <see langword="null"/> for a table of version 0.</param>
    /// <param name="clipBoxes">Version 1: the clip box of glyphs: left, bottom, right, top.</param>
    public static byte[] WriteColr(
        SortedDictionary<int, (int Glyph, int PaletteIndex)[]> layers,
        SortedDictionary<int, FontColorPaint>? paints = null,
        SortedDictionary<int, (int XMin, int YMin, int XMax, int YMax)>? clipBoxes = null)
    {
        var layerCount = layers.Values.Sum(glyphLayers => glyphLayers.Length);
        var headerLength = paints is null ? 14 : 34;
        var baseGlyphsOffset = headerLength;
        var layersOffset = baseGlyphsOffset + (layers.Count * 6);
        var writer = new FontWriter();
        writer.UInt16(paints is null ? 0 : 1);
        writer.UInt16(layers.Count);
        writer.UInt32((uint)baseGlyphsOffset);
        writer.UInt32((uint)layersOffset);
        writer.UInt16(layerCount);

        var version1 = new FontWriter();
        if (paints is not null)
        {
            // The list of the glyphs and their paints, then the clip boxes. The layers of a PaintColrLayers are written
            // as the paints that follow it, through a layer list that is written last.
            var listOffset = layersOffset + (layerCount * 4);
            var layerList = new List<FontColorPaint>();
            var paintData = new FontWriter();
            var paintOffsets = new List<int>();
            var recordsLength = 4 + (paints.Count * 6);
            foreach (var paint in paints.Values)
            {
                paintOffsets.Add(recordsLength + paintData.Length);
                paintData.Bytes(WritePaint(paint, layerList));
            }

            version1.UInt32((uint)paints.Count);
            var index = 0;
            foreach (var glyph in paints.Keys)
            {
                version1.UInt16(glyph);
                version1.UInt32((uint)paintOffsets[index++]);
            }

            version1.Bytes(paintData.ToArray());

            // The layer list: each layer can add layers of its own, which follow.
            var layerListOffset = listOffset + version1.Length;
            var layerPaints = new List<byte[]>();
            for (var i = 0; i < layerList.Count; i++)
            {
                layerPaints.Add(WritePaint(layerList[i], layerList));
            }

            var layerWriter = new FontWriter();
            layerWriter.UInt32((uint)layerPaints.Count);
            var layerPaintOffset = 4 + (layerPaints.Count * 4);
            foreach (var data in layerPaints)
            {
                layerWriter.UInt32((uint)layerPaintOffset);
                layerPaintOffset += data.Length;
            }

            foreach (var data in layerPaints)
            {
                layerWriter.Bytes(data);
            }

            var clipListOffset = 0;
            var clipWriter = new FontWriter();
            if (clipBoxes is { Count: > 0 })
            {
                clipListOffset = layerListOffset + layerWriter.Length;
                clipWriter.Byte(1);
                clipWriter.UInt32((uint)clipBoxes.Count);
                var boxOffset = 5 + (clipBoxes.Count * 7);
                foreach (var glyph in clipBoxes.Keys)
                {
                    clipWriter.UInt16(glyph);
                    clipWriter.UInt16(glyph);
                    clipWriter.UInt24(boxOffset);
                    boxOffset += 9;
                }

                foreach (var (xMin, yMin, xMax, yMax) in clipBoxes.Values)
                {
                    clipWriter.Byte(1);
                    clipWriter.Int16(xMin);
                    clipWriter.Int16(yMin);
                    clipWriter.Int16(xMax);
                    clipWriter.Int16(yMax);
                }
            }

            writer.UInt32((uint)listOffset);
            writer.UInt32((uint)layerListOffset);
            writer.UInt32((uint)clipListOffset);
            writer.UInt32(0);
            writer.UInt32(0);
            version1.Bytes(layerWriter.ToArray());
            version1.Bytes(clipWriter.ToArray());
        }

        var firstLayer = 0;
        foreach (var (glyph, glyphLayers) in layers)
        {
            writer.UInt16(glyph);
            writer.UInt16(firstLayer);
            writer.UInt16(glyphLayers.Length);
            firstLayer += glyphLayers.Length;
        }

        foreach (var (glyph, paletteIndex) in layers.Values.SelectMany(glyphLayers => glyphLayers))
        {
            writer.UInt16(glyph);
            writer.UInt16(paletteIndex);
        }

        writer.Bytes(version1.ToArray());
        return writer.ToArray();
    }

    /// <summary>Writes a paint table, followed by the tables it refers to.</summary>
    private static byte[] WritePaint(FontColorPaint paint, List<FontColorPaint> layerList)
    {
        var writer = new FontWriter();
        switch (paint)
        {
            case FontColorPaint.Layers layers:
                writer.Byte(1);
                writer.Byte((byte)layers.Paints.Length);
                writer.UInt32((uint)layerList.Count);
                layerList.AddRange(layers.Paints);
                break;

            case FontColorPaint.Solid solid:
                writer.Byte(2);
                writer.UInt16(solid.PaletteIndex);
                writer.F2Dot14(solid.Alpha);
                break;

            case FontColorPaint.Linear linear:
                writer.Byte(4);
                writer.UInt24(16);
                writer.Int16(linear.X0);
                writer.Int16(linear.Y0);
                writer.Int16(linear.X1);
                writer.Int16(linear.Y1);
                writer.Int16(linear.X2);
                writer.Int16(linear.Y2);
                WriteColorLine(writer, linear.Line);
                break;

            case FontColorPaint.Radial radial:
                writer.Byte(6);
                writer.UInt24(16);
                writer.Int16(radial.X0);
                writer.Int16(radial.Y0);
                writer.UInt16(radial.Radius0);
                writer.Int16(radial.X1);
                writer.Int16(radial.Y1);
                writer.UInt16(radial.Radius1);
                WriteColorLine(writer, radial.Line);
                break;

            case FontColorPaint.Sweep sweep:
                // The angles are stored as half turns minus one.
                writer.Byte(8);
                writer.UInt24(12);
                writer.Int16(sweep.CenterX);
                writer.Int16(sweep.CenterY);
                writer.F2Dot14((sweep.StartAngle / 180) - 1);
                writer.F2Dot14((sweep.EndAngle / 180) - 1);
                WriteColorLine(writer, sweep.Line);
                break;

            case FontColorPaint.Glyph glyph:
                writer.Byte(10);
                writer.UInt24(6);
                writer.UInt16(glyph.GlyphId);
                writer.Bytes(WritePaint(glyph.Paint, layerList));
                break;

            case FontColorPaint.ColrGlyph colrGlyph:
                writer.Byte(11);
                writer.UInt16(colrGlyph.GlyphId);
                break;

            case FontColorPaint.Transform transform:
                {
                    // The matrix follows the paint table, and the transformed paint follows the matrix.
                    writer.Byte(12);
                    writer.UInt24(7 + 24);
                    writer.UInt24(7);
                    foreach (var value in (double[])[transform.XX, transform.YX, transform.XY, transform.YY, transform.DX, transform.DY])
                    {
                        writer.Int32((int)Math.Round(value * 65536, MidpointRounding.AwayFromZero));
                    }

                    writer.Bytes(WritePaint(transform.Paint, layerList));
                    break;
                }

            case FontColorPaint.Translate translate:
                writer.Byte(14);
                writer.UInt24(8);
                writer.Int16(translate.DX);
                writer.Int16(translate.DY);
                writer.Bytes(WritePaint(translate.Paint, layerList));
                break;

            case FontColorPaint.Scale scale:
                {
                    // The four formats: uniform or not, around the origin or around a center.
                    var isUniform = scale.ScaleX == scale.ScaleY;
                    var hasCenter = scale.CenterX != 0 || scale.CenterY != 0;
                    writer.Byte((byte)((isUniform ? 20 : 16) + (hasCenter ? 2 : 0)));
                    writer.UInt24(4 + (isUniform ? 2 : 4) + (hasCenter ? 4 : 0));
                    writer.F2Dot14(scale.ScaleX);
                    if (!isUniform)
                    {
                        writer.F2Dot14(scale.ScaleY);
                    }

                    if (hasCenter)
                    {
                        writer.Int16(scale.CenterX);
                        writer.Int16(scale.CenterY);
                    }

                    writer.Bytes(WritePaint(scale.Paint, layerList));
                    break;
                }

            case FontColorPaint.Rotate rotate:
                {
                    var hasCenter = rotate.CenterX != 0 || rotate.CenterY != 0;
                    writer.Byte((byte)(hasCenter ? 26 : 24));
                    writer.UInt24(hasCenter ? 10 : 6);
                    writer.F2Dot14(rotate.Angle / 180);
                    if (hasCenter)
                    {
                        writer.Int16(rotate.CenterX);
                        writer.Int16(rotate.CenterY);
                    }

                    writer.Bytes(WritePaint(rotate.Paint, layerList));
                    break;
                }

            case FontColorPaint.Skew skew:
                {
                    var hasCenter = skew.CenterX != 0 || skew.CenterY != 0;
                    writer.Byte((byte)(hasCenter ? 30 : 28));
                    writer.UInt24(hasCenter ? 12 : 8);
                    writer.F2Dot14(skew.XAngle / 180);
                    writer.F2Dot14(skew.YAngle / 180);
                    if (hasCenter)
                    {
                        writer.Int16(skew.CenterX);
                        writer.Int16(skew.CenterY);
                    }

                    writer.Bytes(WritePaint(skew.Paint, layerList));
                    break;
                }

            case FontColorPaint.Composite composite:
                {
                    var source = WritePaint(composite.Source, layerList);
                    writer.Byte(32);
                    writer.UInt24(8);
                    writer.Byte((byte)composite.Mode);
                    writer.UInt24(8 + source.Length);
                    writer.Bytes(source);
                    writer.Bytes(WritePaint(composite.Backdrop, layerList));
                    break;
                }

            case FontColorPaint.Unknown unknown:
                writer.Byte((byte)unknown.Format);
                writer.Zeros(8);
                break;

            default:
                throw new InvalidOperationException("Unknown paint");
        }

        return writer.ToArray();
    }

    private static void WriteColorLine(FontWriter writer, FontColorLine line)
    {
        writer.Byte((byte)line.Extend);
        writer.UInt16(line.Stops.Length);
        foreach (var (offset, paletteIndex, alpha) in line.Stops)
        {
            writer.F2Dot14(offset);
            writer.UInt16(paletteIndex);
            writer.F2Dot14(alpha);
        }
    }
}
