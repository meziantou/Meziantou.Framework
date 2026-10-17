namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The profiles that the encoder announces (Rec. ITU-T H.265, clause A.3): the profiles of the first version of the
/// specification, and the format range extensions profiles of clause A.3.5 for the sample formats and the tools that the
/// first ones do not have.
/// </summary>
/// <remarks>
/// The Main Intra and Main 10 Intra profiles are not in the list: an 8-bit or 10-bit 4:2:0 sequence without the tools of
/// the range extensions conforms to the Main or Main 10 profile, which every decoder has, whether its pictures are all
/// intra pictures or not.
/// </remarks>
internal enum HevcProfile
{
    /// <summary>Main (clause A.3.2): 8-bit 4:2:0.</summary>
    Main,

    /// <summary>Main 10 (clause A.3.3): 4:2:0 of at most 10 bits.</summary>
    Main10,

    /// <summary>Main 10 Still Picture (clause A.3.3): one 4:2:0 picture of at most 10 bits.</summary>
    Main10StillPicture,

    /// <summary>Main Still Picture (clause A.3.4): one 8-bit 4:2:0 picture.</summary>
    MainStillPicture,

    /// <summary>Monochrome (clause A.3.5): 8-bit 4:0:0.</summary>
    Monochrome,

    /// <summary>Monochrome 10: 4:0:0 of at most 10 bits.</summary>
    Monochrome10,

    /// <summary>Monochrome 12: 4:0:0 of at most 12 bits.</summary>
    Monochrome12,

    /// <summary>Main 12: 4:0:0 or 4:2:0 of at most 12 bits.</summary>
    Main12,

    /// <summary>Main 4:2:2 10: 4:0:0, 4:2:0 or 4:2:2 of at most 10 bits.</summary>
    Main42210,

    /// <summary>Main 4:2:2 12: 4:0:0, 4:2:0 or 4:2:2 of at most 12 bits.</summary>
    Main42212,

    /// <summary>Main 4:4:4: any chroma format, 8 bits, with the tools of the range extensions.</summary>
    Main444,

    /// <summary>Main 4:4:4 10: any chroma format, at most 10 bits.</summary>
    Main44410,

    /// <summary>Main 4:4:4 12: any chroma format, at most 12 bits.</summary>
    Main44412,

    /// <summary>Main 12 Intra: Main 12 whose pictures are all intra random access point pictures.</summary>
    Main12Intra,

    /// <summary>Main 4:2:2 10 Intra.</summary>
    Main42210Intra,

    /// <summary>Main 4:2:2 12 Intra.</summary>
    Main42212Intra,

    /// <summary>Main 4:4:4 Intra.</summary>
    Main444Intra,

    /// <summary>Main 4:4:4 10 Intra.</summary>
    Main44410Intra,

    /// <summary>Main 4:4:4 12 Intra.</summary>
    Main44412Intra,

    /// <summary>Main 4:4:4 Still Picture: one 8-bit picture of any chroma format.</summary>
    Main444StillPicture,
}
