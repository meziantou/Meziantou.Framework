namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What a sequential reader call asks the decoder to produce.</summary>
internal enum SequentialRequest
{
    /// <summary>No call is in progress (opening the reader: only the header may be parsed).</summary>
    None,

    /// <summary><c>ReadPosterFrame</c>: the next image must be the separate poster announced by the header.</summary>
    Poster,

    /// <summary><c>ReadFrame</c>/<c>ReadFrameInto</c>: the next displayed frame. A poster met first is decoded and discarded.</summary>
    Frame,
}
