namespace Meziantou.Framework.Fonts;

/// <summary>
/// A face of a font installed on the machine: the file that holds it, and its names and its style, read without loading
/// the font.
/// </summary>
/// <remarks>
/// A descriptor is immutable. To use the face, load <see cref="FilePath"/> and take the face at <see cref="FaceIndex"/>.
/// The file may have changed or been removed since the fonts were scanned: loading it can fail.
/// </remarks>
public sealed class SystemFontDescriptor
{
    private readonly FontAxisRecord[] _axes;

    internal SystemFontDescriptor(string filePath, int faceIndex, FaceStyle style, string[] familyNames, string? fullName, int macStyle, FontAxisRecord[] axes)
    {
        FilePath = filePath;
        FaceIndex = faceIndex;
        FamilyName = style.FamilyName;
        FamilyNames = familyNames;
        SubfamilyName = style.SubfamilyName;
        FullName = fullName;
        PostScriptName = style.PostScriptName;
        HasOs2 = style.HasOs2;
        Weight = style.Weight;
        WidthClass = style.WidthClass;
        Selection = style.Selection;
        MacStyle = macStyle;
        IsItalic = style.IsItalic;
        ItalicAngle = style.ItalicAngle;
        IsMonospaced = style.IsMonospaced;
        _axes = axes;
    }

    /// <summary>Gets the full path of the font file.</summary>
    public string FilePath { get; }

    /// <summary>Gets the index of the face in the file: 0, or its index in a font collection.</summary>
    public int FaceIndex { get; }

    /// <summary>Gets the preferred family name: the typographic family name, else the family name, else an empty string.</summary>
    /// <remarks>It is <see cref="FontFace.FamilyName"/> of the face.</remarks>
    public string FamilyName { get; }

    /// <summary>
    /// Gets the names the face is found by in <see cref="SystemFontCollection.GetFamily(string)"/>: the typographic family
    /// names and the family names, in every language of the <c>name</c> table, each one once.
    /// </summary>
    public IReadOnlyList<string> FamilyNames { get; }

    /// <summary>Gets the preferred subfamily name, such as <c>Bold Italic</c>, else an empty string.</summary>
    public string SubfamilyName { get; }

    /// <summary>Gets the full name of the face (name 4 of the <c>name</c> table), or <see langword="null"/> when the font has none.</summary>
    public string? FullName { get; }

    /// <summary>Gets the PostScript name of the face, or <see langword="null"/> when the font has none.</summary>
    public string? PostScriptName { get; }

    /// <summary>Gets whether the face has an <c>OS/2</c> table.</summary>
    public bool HasOs2 { get; }

    /// <summary>Gets the weight class: <c>OS/2.usWeightClass</c>, else 700 or 400 from <c>head.macStyle</c>.</summary>
    public int Weight { get; }

    /// <summary>Gets the width class, <c>OS/2.usWidthClass</c>: from 1 (ultra-condensed) to 9 (ultra-expanded), 5 without an <c>OS/2</c> table.</summary>
    public int WidthClass { get; }

    /// <summary>Gets the <c>OS/2.fsSelection</c> field, 0 without an <c>OS/2</c> table.</summary>
    public int Selection { get; }

    /// <summary>Gets the <c>head.macStyle</c> field.</summary>
    public int MacStyle { get; }

    /// <summary>Gets whether the face is italic: bit 0 of <c>OS/2.fsSelection</c>, else bit 1 of <c>head.macStyle</c>.</summary>
    public bool IsItalic { get; }

    /// <summary>Gets whether the face is oblique rather than italic: bit 9 of <c>OS/2.fsSelection</c>.</summary>
    public bool IsOblique => (Selection & 0x0200) != 0;

    /// <summary>Gets the italic angle of the <c>post</c> table, in degrees counter-clockwise from the vertical.</summary>
    public double ItalicAngle { get; }

    /// <summary>Gets whether the face is monospaced (<c>post.isFixedPitch</c>).</summary>
    public bool IsMonospaced { get; }

    /// <summary>Gets the axes of a variable font, empty for a font that is not variable.</summary>
    public ReadOnlySpan<FontAxisRecord> Axes => _axes;

    internal bool TryGetAxis(uint tag, out FontAxisRecord axis)
    {
        foreach (var candidate in _axes)
        {
            if (candidate.Tag == tag)
            {
                axis = candidate;
                return true;
            }
        }

        axis = default;
        return false;
    }

    /// <inheritdoc />
    public override string ToString() => $"{FullName ?? FamilyName} ({FilePath}, {FaceIndex})";
}
