using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.Pdf.VectorGraphics.Internal;

/// <summary>
/// Lays a text out in clusters, from left to right: the glyphs of an embedded font are shaped in runs of one script and
/// one direction, ordered by the Unicode Bidirectional Algorithm, as the PDF fonts do; a standard font has one cluster
/// per grapheme cluster. The letter spacing is added after each cluster and the word spacing after each space, as the
/// image renderer adds them: on the side the text continues to.
/// </summary>
internal static class TextClusterLayout
{
    // The optional ligatures are not formed with a letter spacing, as in CSS
    private static readonly ShapingFeature[] NoLigatures = [new("liga", 0), new("clig", 0), new("dlig", 0)];

    // The shaping of the faces, which a scene shares between the documents that draw it, one text at a time
    private static readonly Lock ShapingLock = new();

    /// <summary>Lays a text out.</summary>
    /// <param name="font">The PDF font.</param>
    /// <param name="face">The face of an embedded font, or <see langword="null"/> for a standard font.</param>
    /// <param name="text">The text, that the font can encode.</param>
    /// <param name="size">The size of the font.</param>
    /// <param name="letterSpacing">The space added after each cluster.</param>
    /// <param name="wordSpacing">The space added after each space.</param>
    /// <returns>The clusters from left to right, and the width of the text.</returns>
    public static (List<TextCluster> Clusters, double Width) Layout(PdfFont font, FontFace? face, string text, double size, double letterSpacing, double wordSpacing)
    {
        return face is null ? LayoutStandard(font, text, size, letterSpacing, wordSpacing) : LayoutEmbedded(face, text, size, letterSpacing, wordSpacing);
    }

    private static (List<TextCluster> Clusters, double Width) LayoutStandard(PdfFont font, string text, double size, double letterSpacing, double wordSpacing)
    {
        var clusters = new List<TextCluster>();
        var x = 0.0;
        foreach (var range in Unicode.EnumerateGraphemeClusters(text))
        {
            var cluster = text[range];
            var spacing = GetSpacing(cluster[0], letterSpacing, wordSpacing);
            var advance = font.GetTextWidth(cluster, size) + spacing;
            clusters.Add(new TextCluster(cluster, x, advance, spacing, Glyphs: null));
            x += advance;
        }

        return (clusters, x);
    }

    private static (List<TextCluster> Clusters, double Width) LayoutEmbedded(FontFace face, string text, double size, double letterSpacing, double wordSpacing)
    {
        var clusters = new List<TextCluster>();
        if (text.Length is 0)
            return (clusters, 0);

        var levels = new byte[text.Length];
        UnicodeBidi.ResolveLevels(text, UnicodeBidiDirection.Auto, levels);
        var runs = new List<(int Start, int Length, UnicodeScript Script)>();
        var runLevels = new List<byte>();
        foreach (var scriptRun in Unicode.EnumerateScriptRuns(text))
        {
            var end = scriptRun.Index + scriptRun.Length;
            var start = scriptRun.Index;
            while (start < end)
            {
                var next = start + 1;
                while (next < end && levels[next] == levels[start])
                {
                    next++;
                }

                runs.Add((start, next - start, scriptRun.Script));
                runLevels.Add(levels[start]);
                start = next;
            }
        }

        var order = new int[runs.Count];
        UnicodeBidi.GetVisualOrder(runLevels.ToArray(), order);
        var scale = size / face.UnitsPerEm;
        var features = letterSpacing is 0 ? [] : NoLigatures;
        var x = 0.0;
        lock (ShapingLock)
        {
            var buffer = new GlyphBuffer(face.Data.Environment.Limits);
            foreach (var index in order)
            {
                var (start, length, script) = runs[index];
                var isRightToLeft = (runLevels[index] & 1) != 0;
                buffer.Clear();
                TextShaper.AddText(buffer, text.AsSpan(start, length), start);
                TextShaper.Shape(buffer, face, script, [], isRightToLeft, features);

                var info = buffer.Info;
                var positions = buffer.Positions;
                // The starts of the clusters, in logical order: a cluster ends where the next one starts
                var clusterStarts = new int[info.Length];
                for (var k = 0; k < info.Length; k++)
                {
                    clusterStarts[k] = info[k].Cluster;
                }

                Array.Sort(clusterStarts);

                for (var i = 0; i < info.Length;)
                {
                    // The glyphs of a cluster are consecutive
                    var cluster = info[i].Cluster;
                    var end = i + 1;
                    while (end < info.Length && info[end].Cluster == cluster)
                    {
                        end++;
                    }

                    var clusterEnd = GetNextClusterStart(clusterStarts, cluster) ?? (start + length);
                    var spacing = GetSpacing(text[cluster], letterSpacing, wordSpacing);

                    // In a right-to-left run, the glyphs are in display order: the space goes before the cluster
                    var offset = isRightToLeft ? spacing : 0;
                    var glyphs = new List<PdfGlyph>(end - i);
                    var pen = 0.0;
                    for (var j = i; j < end; j++)
                    {
                        var position = positions[j];

                        // A character without glyph is not shown (the text is sanitized: it is an invisible character); its advance is kept
                        if (info[j].Glyph != 0)
                        {
                            glyphs.Add(new PdfGlyph(info[j].Glyph, offset + pen + (position.XOffset * scale), position.YOffset * scale));
                        }

                        pen += position.XAdvance * scale;
                    }

                    var advance = pen + spacing;
                    clusters.Add(new TextCluster(text[cluster..clusterEnd], x, advance, spacing, [.. glyphs]));
                    x += advance;
                    i = end;
                }
            }
        }

        return (clusters, x);
    }

    // The first start greater than a cluster, by binary search
    private static int? GetNextClusterStart(int[] sortedStarts, int cluster)
    {
        var (low, high) = (0, sortedStarts.Length);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (sortedStarts[middle] <= cluster)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low < sortedStarts.Length ? sortedStarts[low] : null;
    }

    private static double GetSpacing(char first, double letterSpacing, double wordSpacing) => letterSpacing + (first is ' ' or '\u00A0' ? wordSpacing : 0);
}
