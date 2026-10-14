namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What shaping computes from a font alone, kept for the runs of one layout: the glyphs each lookup of its layout tables
/// may apply at. A font has no cache of its own; this one lives as long as the engine that lays out a text.
/// </summary>
internal sealed class ShapingCache
{
    private readonly Dictionary<FontFace, (LookupDigests? Substitutions, LookupDigests? Positions)> _digests = [];

    /// <summary>Gets the digests of the lookups of the GSUB or GPOS table of a font, which has the table.</summary>
    public LookupDigests GetDigests(FontFace face, bool isPositioning)
    {
        _digests.TryGetValue(face, out var entry);
        var digests = isPositioning ? entry.Positions : entry.Substitutions;
        if (digests is null)
        {
            digests = Create(face, isPositioning);
            _digests[face] = isPositioning ? (entry.Substitutions, digests) : (digests, entry.Positions);
        }

        return digests;
    }

    /// <summary>Creates the digests of the lookups of the GSUB or GPOS table of a font, which has the table.</summary>
    public static LookupDigests Create(FontFace face, bool isPositioning)
        => isPositioning ? new LookupDigests(face.Gpos!, face.GposData, isPositioning: true) : new LookupDigests(face.Gsub!, face.GsubData, isPositioning: false);
}
