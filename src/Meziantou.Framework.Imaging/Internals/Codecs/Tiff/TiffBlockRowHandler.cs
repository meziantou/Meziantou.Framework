namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The destination of the rows of a TIFF strip that is decoded row by row (<see cref="TiffBlockReader.ReadJpegStrip"/>).</summary>
internal abstract class TiffBlockRowHandler
{
    /// <summary>Receives one decoded row of the strip, from the top one down.</summary>
    /// <param name="row">The index of the row in the strip.</param>
    /// <param name="samples">The samples of the row: <see cref="TiffPage.BlockRowLength"/> bytes, only valid during the call.</param>
    public abstract void OnRow(int row, ReadOnlySpan<byte> samples);
}
