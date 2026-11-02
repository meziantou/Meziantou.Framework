using System.Buffers.Binary;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// A TrueType font program embedded as <c>/FontFile2</c>: the outlines are decoded by the <c>glyf</c> reader of
/// Meziantou.Framework.Fonts. The programs of PDF files often have only the tables a PDF reader needs (ISO 32000-2,
/// 9.9): the directory is read here, and only <c>head</c>, <c>loca</c> and <c>glyf</c> are required. The glyphs keep
/// their coordinates: the origin is at 0, whatever the left side bearings say.
/// </summary>
internal sealed class TrueTypeFontProgram
{
    private const int MaxGlyphs = 65536;

    private readonly byte[] _loca;
    private readonly byte[] _glyf;
    private readonly byte[] _hmtx;
    private readonly bool _longLocations;
    private readonly Dictionary<(int Platform, int Encoding), TrueTypeCmapSubtable> _cmaps = [];
    private readonly Dictionary<string, int> _postNames = new(StringComparer.Ordinal);
    private readonly RenderCache _cache;
    private readonly GlyphScratch _scratch;
    private readonly TrueTypeGlyphOutlines _outlines;

    private TrueTypeFontProgram(Dictionary<uint, byte[]> tables, RenderCache cache)
    {
        _cache = cache;
        var head = tables.GetValueOrDefault(Tag("head")) ?? throw new InvalidPdfContentException("The TrueType font program has no 'head' table.");
        _loca = tables.GetValueOrDefault(Tag("loca")) ?? throw new InvalidPdfContentException("The TrueType font program has no 'loca' table.");
        _glyf = tables.GetValueOrDefault(Tag("glyf")) ?? throw new InvalidPdfContentException("The TrueType font program has no 'glyf' table.");
        if (head.Length < 54)
            throw new InvalidPdfContentException("The 'head' table of the TrueType font program is too short.");

        UnitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(18));
        if (UnitsPerEm is < 16 or > 16384)
        {
            UnitsPerEm = 1000;
        }

        _longLocations = BinaryPrimitives.ReadInt16BigEndian(head.AsSpan(50)) != 0;
        var locaCount = (_loca.Length / (_longLocations ? 4 : 2)) - 1;
        var maxp = tables.GetValueOrDefault(Tag("maxp"));
        var glyphCount = maxp is { Length: >= 6 } ? BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4)) : locaCount;
        GlyphCount = Math.Clamp(Math.Min(glyphCount, locaCount), 0, MaxGlyphs);

        // The horizontal metrics the reader places the outlines with: a left side bearing equal to the left of each
        // glyph, so that the outlines keep their coordinates
        _hmtx = new byte[Math.Max(1, GlyphCount) * 4];
        for (var glyph = 0; glyph < GlyphCount; glyph++)
        {
            var (start, end) = GetGlyphRange(glyph);
            if (end - start >= 10)
            {
                BinaryPrimitives.WriteInt16BigEndian(_hmtx.AsSpan((glyph * 4) + 2), BinaryPrimitives.ReadInt16BigEndian(_glyf.AsSpan(start + 2)));
            }
        }

        if (tables.GetValueOrDefault(Tag("cmap")) is { } cmap)
        {
            ReadCmap(cmap);
        }

        if (tables.GetValueOrDefault(Tag("post")) is { } post)
        {
            ReadPost(post);
        }

        _scratch = cache.CreateGlyphScratch(cache.FontEnvironment, "PDF TrueType font");
        _outlines = new TrueTypeGlyphOutlines(_glyf, _loca, _longLocations, GlyphCount, _hmtx, GlyphCount);
    }

    public int GlyphCount { get; }

    public int UnitsPerEm { get; }

    /// <summary>Gets the matrix from the units of the glyphs to text space.</summary>
    public PdfMatrix FontMatrix => new(1.0 / UnitsPerEm, 0, 0, 1.0 / UnitsPerEm, 0, 0);

    public static TrueTypeFontProgram Read(byte[] data, RenderCache cache)
    {
        if (data.Length < 12)
            throw new InvalidPdfContentException("The TrueType font program is too short.");

        var offset = 0;
        var version = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (version == Tag("ttcf"))
        {
            // A collection: its first font
            if (data.Length < 16)
                throw new InvalidPdfContentException("The TrueType collection is too short.");

            offset = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12)), int.MaxValue);
        }

        if (offset + 12 > data.Length)
            throw new InvalidPdfContentException("The TrueType font program is truncated.");

        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 4));
        var tables = new Dictionary<uint, byte[]>();
        for (var i = 0; i < tableCount; i++)
        {
            var record = offset + 12 + (i * 16);
            if (record + 16 > data.Length)
                break;

            var tag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record));
            var tableOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8));
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 12));
            if (tableOffset >= data.Length)
                continue;

            // A table that passes the end of the data is kept up to the end
            var available = (int)Math.Min(length, data.Length - tableOffset);
            tables.TryAdd(tag, data.AsSpan((int)tableOffset, available).ToArray());
        }

        return new TrueTypeFontProgram(tables, cache);
    }

    /// <summary>Gets the glyph of a character in a subtable of the <c>cmap</c> table; 0 when there is none.</summary>
    public int MapCharacter(int platform, int encoding, int character) => _cmaps.TryGetValue((platform, encoding), out var map) ? map.Map(character) : 0;

    public bool HasCmap(int platform, int encoding) => _cmaps.ContainsKey((platform, encoding));

    public int GetGlyphByPostName(string name) => _postNames.GetValueOrDefault(name);

    public VectorPath? GetPath(int glyph)
    {
        if ((uint)glyph >= (uint)GlyphCount)
            return null;

        var sink = new GlyphPathSink();
        var budget = new GlyphBudget(_cache.FontEnvironment.Limits);
        _outlines.DecodeOutline(glyph, sink, _scratch, ref budget);
        return sink.ToPath();
    }

    private (int Start, int End) GetGlyphRange(int glyph)
    {
        int start;
        int end;
        if (_longLocations)
        {
            start = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(_loca.AsSpan(glyph * 4)), int.MaxValue);
            end = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(_loca.AsSpan((glyph + 1) * 4)), int.MaxValue);
        }
        else
        {
            start = BinaryPrimitives.ReadUInt16BigEndian(_loca.AsSpan(glyph * 2)) * 2;
            end = BinaryPrimitives.ReadUInt16BigEndian(_loca.AsSpan((glyph + 1) * 2)) * 2;
        }

        return start <= end && end <= _glyf.Length ? (start, end) : (0, 0);
    }

    /// <summary>Reads the subtables of the <c>cmap</c> table (see <see cref="TrueTypeCmapSubtable"/>): the first one of each platform and encoding.</summary>
    private void ReadCmap(byte[] cmap)
    {
        if (cmap.Length < 4)
            return;

        var count = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(2));
        for (var i = 0; i < count; i++)
        {
            var record = 4 + (i * 8);
            if (record + 8 > cmap.Length)
                break;

            var platform = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(record));
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(record + 2));
            var offset = BinaryPrimitives.ReadUInt32BigEndian(cmap.AsSpan(record + 4));
            if (offset + 4 > cmap.Length || _cmaps.ContainsKey((platform, encoding)))
                continue;

            if (TrueTypeCmapSubtable.Read(cmap, (int)offset) is { } subtable)
            {
                _cmaps[(platform, encoding)] = subtable;
            }
        }
    }

    /// <summary>Reads the glyph names of a <c>post</c> table of format 2: the standard Macintosh names, and the names of the table.</summary>
    private void ReadPost(byte[] post)
    {
        if (post.Length < 34 || BinaryPrimitives.ReadUInt32BigEndian(post) != 0x00020000)
            return;

        var count = BinaryPrimitives.ReadUInt16BigEndian(post.AsSpan(32));
        if (34 + (count * 2) > post.Length)
            return;

        // The names after the indexes: Pascal strings
        var names = new List<string>();
        var position = 34 + (count * 2);
        while (position < post.Length)
        {
            var length = post[position++];
            if (position + length > post.Length)
                break;

            names.Add(System.Text.Encoding.Latin1.GetString(post, position, length));
            position += length;
        }

        for (var glyph = 0; glyph < count; glyph++)
        {
            var index = BinaryPrimitives.ReadUInt16BigEndian(post.AsSpan(34 + (glyph * 2)));
            var name = index < MacintoshGlyphNames.Names.Length ? MacintoshGlyphNames.Names[index] : index - MacintoshGlyphNames.Names.Length < names.Count ? names[index - MacintoshGlyphNames.Names.Length] : null;
            if (name is not null)
            {
                _postNames.TryAdd(name, glyph);
            }
        }
    }

    private static uint Tag(string tag) => ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
}
