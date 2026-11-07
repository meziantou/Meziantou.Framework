using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The hinted outlines of the glyphs of a layout, in font units, by font, glyph and size.</summary>
internal sealed class HintedOutlines(RecordedOutlines outlines, Dictionary<(Font Font, int Glyph, double Size), int> indexes)
{
    /// <summary>Sends the hinted outline of a glyph to a sink.</summary>
    /// <returns><see langword="false"/> when the glyph was not hinted.</returns>
    public bool TryReplay(Font font, int glyph, double size, OutlineSink sink)
    {
        if (!indexes.TryGetValue((font, glyph, size), out var index))
            return false;

        outlines.Replay(index, sink);
        return true;
    }
}
