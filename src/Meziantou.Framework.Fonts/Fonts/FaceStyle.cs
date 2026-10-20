using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// The names and the style of a face: what <see cref="FontFace"/> declares and what <see cref="SystemFontCollection"/>
/// indexes without loading the font, read by the same code so that both always agree.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct FaceStyle
{
    /// <summary>The length of the fields of version 0 of the OS/2 table, which every version has.</summary>
    public const int MinOs2Length = 78;

    /// <summary>The length of the fields of the post table that are read.</summary>
    public const int MinPostLength = 16;

    /// <summary>The length of the header of the name table.</summary>
    public const int MinNameLength = 6;

    public bool HasOs2 { get; init; }

    public int Weight { get; init; }

    public int WidthClass { get; init; }

    public int Selection { get; init; }

    public bool IsItalic { get; init; }

    public bool HasPost { get; init; }

    public double ItalicAngle { get; init; }

    public bool IsMonospaced { get; init; }

    public string FamilyName { get; init; }

    public string SubfamilyName { get; init; }

    public string? PostScriptName { get; init; }

    /// <summary>Reads the style from the tables of a face; a table that is absent or too short to be read is empty.</summary>
    /// <param name="macStyle">The <c>macStyle</c> field of the <c>head</c> table.</param>
    /// <param name="os2">The <c>OS/2</c> table.</param>
    /// <param name="post">The <c>post</c> table.</param>
    /// <param name="name">The <c>name</c> table.</param>
    public static FaceStyle Read(int macStyle, ReadOnlySpan<byte> os2, ReadOnlySpan<byte> post, ReadOnlySpan<byte> name)
    {
        // OS/2: optional in old fonts. Without it, the weight and the slope come from head.macStyle.
        var hasOs2 = os2.Length >= MinOs2Length;
        var selection = hasOs2 ? FontBinary.UInt16(os2, 62) : 0;
        var hasPost = post.Length >= MinPostLength;
        var hasName = name.Length >= MinNameLength;
        return new FaceStyle
        {
            HasOs2 = hasOs2,
            Weight = hasOs2 ? FontBinary.UInt16(os2, 4) : (macStyle & 0x0001) != 0 ? 700 : 400,
            WidthClass = hasOs2 ? FontBinary.UInt16(os2, 6) : 5,
            Selection = selection,
            IsItalic = hasOs2 ? (selection & 0x0001) != 0 : (macStyle & 0x0002) != 0,
            HasPost = hasPost,
            ItalicAngle = hasPost ? FontBinary.Int32(post, 4) / 65536.0 : 0,
            IsMonospaced = hasPost && FontBinary.UInt32(post, 12) != 0,
            FamilyName = hasName ? NameTable.Find(name, NameTable.TypographicFamilyName) ?? NameTable.Find(name, NameTable.FamilyName) ?? "" : "",
            SubfamilyName = hasName ? NameTable.Find(name, NameTable.TypographicSubfamilyName) ?? NameTable.Find(name, NameTable.SubfamilyName) ?? "" : "",
            PostScriptName = hasName ? NameTable.Find(name, NameTable.PostScriptName) : null,
        };
    }
}
