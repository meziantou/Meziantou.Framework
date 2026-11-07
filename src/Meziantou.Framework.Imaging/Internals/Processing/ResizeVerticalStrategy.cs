namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How the vertical pass of a filtered resize combines horizontally resampled rows.</summary>
internal enum ResizeVerticalStrategy
{
    /// <summary>A ring of the last horizontally resampled source rows; each output row is gathered from its window.</summary>
    Gather = 0,

    /// <summary>One accumulator per output row in progress; each horizontally resampled source row is scattered into them.</summary>
    Scatter = 1,
}
