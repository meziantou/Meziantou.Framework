namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What the decoding of a substream of a slice segment by a worker of <see cref="HevcSubstreamWorkers"/> leaves.</summary>
internal enum HevcSubstreamOutcome
{
    /// <summary>The substream was not decoded, or its decoding stopped because one before it failed or ended the slice segment.</summary>
    None,

    /// <summary>The substream ends the slice segment.</summary>
    EndOfSliceSegment,

    /// <summary>The substream ends at the end of its tile or of its row: the next substream follows it.</summary>
    EndOfSubset,

    /// <summary>The decoding of the substream failed.</summary>
    Failed,
}
