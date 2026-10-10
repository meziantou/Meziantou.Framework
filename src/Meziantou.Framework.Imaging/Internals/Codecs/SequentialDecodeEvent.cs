namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The step a sequential decoder stopped at.</summary>
internal enum SequentialDecodeEvent
{
    /// <summary>The header snapshot is available (<see cref="SequentialDecodeSession.HeaderInfo"/>); no pixel was produced.</summary>
    Header,

    /// <summary>An image (poster or displayed frame) is complete (<see cref="SequentialDecodeSession.TakeImage"/>).</summary>
    Image,

    /// <summary>The clean end of the input: the container was fully validated (or the walk stopped at the frame limit).</summary>
    End,
}
