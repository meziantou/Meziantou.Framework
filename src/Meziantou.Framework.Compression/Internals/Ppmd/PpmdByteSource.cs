namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>The bytes a PPMd range decoder reads.</summary>
internal abstract class PpmdByteSource
{
    /// <summary>Reads the next byte, or returns -1 at the end of the data.</summary>
    public abstract int ReadByte();
}
