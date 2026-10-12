namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How <see cref="JpegXlEntropyEncoder"/> writes the tokens of a stream.</summary>
internal enum JpegXlEntropyCoding
{
    /// <summary>Prefix codes: at least one bit per value of a cluster that holds two tokens or more, and a short description.</summary>
    PrefixCodes,

    /// <summary>The ANS coder: a fraction of a bit for a value that is almost certain, and a longer description.</summary>
    Ans,

    /// <summary>Whichever of the two gives the smaller stream, its description included.</summary>
    Smallest,
}
