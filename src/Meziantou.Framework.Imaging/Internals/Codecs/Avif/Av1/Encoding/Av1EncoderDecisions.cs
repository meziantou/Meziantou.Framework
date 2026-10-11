using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The limits of the search of the encoder for a frame: which candidates are evaluated, and how a rate compares with a
/// distortion. A level is the size of a square block, from 4x4 (0) to 128x128 (5).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Av1EncoderDecisions
{
    /// <summary>The largest square block that is coded without being split.</summary>
    public int MaxBlockLevel;

    /// <summary>The smallest square block that the search reaches by splitting.</summary>
    public int MinBlockLevel;

    /// <summary>Whether the partitions into two rectangles are evaluated.</summary>
    public bool RectangularPartitions;

    /// <summary>The number of luma modes that are encoded, after all of them are ranked by their prediction error.</summary>
    public int LumaModeCandidates;

    /// <summary>The number of chroma modes that are encoded.</summary>
    public int ChromaModeCandidates;

    /// <summary>Whether the angle deltas of the best directional mode are evaluated.</summary>
    public bool AngleDeltas;

    /// <summary>Whether the recursive filter intra modes are evaluated.</summary>
    public bool FilterIntra;

    /// <summary>
    /// The Lagrange multiplier of the rate: the distortion that a bit is worth, in units of 1/256, or 0 for a lossless frame,
    /// which has no distortion.
    /// </summary>
    public long Lambda;

    /// <summary>Returns the cost of a rate in units of 1/256 bit, in the unit of a distortion; the rate itself for a lossless frame.</summary>
    public readonly long GetRateCost(long rate) => Lambda is 0 ? rate : (rate * Lambda) >> 16;

    /// <summary>Returns the limits of an effort, 0 (fastest) to 9 (smallest output).</summary>
    public static Av1EncoderDecisions Create(int effort)
    {
        return effort switch
        {
            <= 0 => new() { MaxBlockLevel = 2, MinBlockLevel = 2, LumaModeCandidates = 1, ChromaModeCandidates = 1 },
            1 => new() { MaxBlockLevel = 3, MinBlockLevel = 1, LumaModeCandidates = 1, ChromaModeCandidates = 1 },
            2 => new() { MaxBlockLevel = 3, MinBlockLevel = 1, LumaModeCandidates = 2, ChromaModeCandidates = 1 },
            3 => new() { MaxBlockLevel = 4, MinBlockLevel = 1, LumaModeCandidates = 2, ChromaModeCandidates = 2, AngleDeltas = true },
            4 => new() { MaxBlockLevel = 4, MinBlockLevel = 1, LumaModeCandidates = 2, ChromaModeCandidates = 2, AngleDeltas = true, RectangularPartitions = true },
            5 => new() { MaxBlockLevel = 4, MinBlockLevel = 0, LumaModeCandidates = 3, ChromaModeCandidates = 2, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true },
            6 => new() { MaxBlockLevel = 4, MinBlockLevel = 0, LumaModeCandidates = 4, ChromaModeCandidates = 3, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true },
            7 => new() { MaxBlockLevel = 5, MinBlockLevel = 0, LumaModeCandidates = 6, ChromaModeCandidates = 4, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true },
            8 => new() { MaxBlockLevel = 5, MinBlockLevel = 0, LumaModeCandidates = 13, ChromaModeCandidates = 6, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true },
            _ => new() { MaxBlockLevel = 5, MinBlockLevel = 0, LumaModeCandidates = 13, ChromaModeCandidates = 13, AngleDeltas = true, RectangularPartitions = true, FilterIntra = true },
        };
    }
}
