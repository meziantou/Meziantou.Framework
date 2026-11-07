namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The magic number of a Netpbm file.</summary>
internal enum PnmVariant
{
    /// <summary><c>P1</c>: plain (ASCII) portable bitmap.</summary>
    PlainBitmap = 1,

    /// <summary><c>P2</c>: plain (ASCII) portable graymap.</summary>
    PlainGrayMap = 2,

    /// <summary><c>P3</c>: plain (ASCII) portable pixmap.</summary>
    PlainPixMap = 3,

    /// <summary><c>P4</c>: binary portable bitmap (packed bits, 1 is black).</summary>
    BinaryBitmap = 4,

    /// <summary><c>P5</c>: binary portable graymap.</summary>
    BinaryGrayMap = 5,

    /// <summary><c>P6</c>: binary portable pixmap.</summary>
    BinaryPixMap = 6,

    /// <summary><c>P7</c>: portable arbitrary map (PAM).</summary>
    ArbitraryMap = 7,
}
