namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Thrown by a video decoder that was given a decode window (<see cref="HevcDecoder.DecodeWindow"/>,
/// <see cref="Av1Decoder.DecodeWindow"/>) when the coded item is not a single intra picture, the only one whose rows below
/// the window are not read by another picture: the caller decodes the item again without the window. It never reaches the
/// caller of the library.
/// </summary>
#pragma warning disable CA1064 // It never leaves the decoders of the HEIF family: they catch it and decode the item whole
internal sealed class DecodeWindowNotApplicableException : Exception
{
    public DecodeWindowNotApplicableException()
        : base("The coded item is not a single intra picture: it is decoded whole.")
    {
    }

    public DecodeWindowNotApplicableException(string message)
        : base(message)
    {
    }

    public DecodeWindowNotApplicableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
#pragma warning restore CA1064
