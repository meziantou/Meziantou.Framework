namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What an <see cref="HevcBinSink"/> does with the bins it is given.</summary>
internal enum HevcBinSinkMode
{
    /// <summary>Nothing: the walk only rebuilds the state of the blocks.</summary>
    None,

    /// <summary>The cost of the bins is added to <see cref="HevcBinSink.Rate"/>: the walk measures a candidate of the search.</summary>
    Cost,

    /// <summary>The bins are given to the arithmetic encoder.</summary>
    Encode,
}
