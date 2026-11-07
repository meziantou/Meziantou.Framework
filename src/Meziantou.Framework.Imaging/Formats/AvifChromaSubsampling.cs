namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the chroma subsampling of lossy color AVIF output.</summary>
public enum AvifChromaSubsampling
{
    /// <summary>4:2:0 (the default): one chroma sample for two columns and two rows.</summary>
    Auto = 0,

    /// <summary>4:4:4: no subsampling.</summary>
    Ratio444 = 1,

    /// <summary>4:2:2: one chroma sample for two columns.</summary>
    Ratio422 = 2,

    /// <summary>4:2:0: one chroma sample for two columns and two rows.</summary>
    Ratio420 = 3,
}
