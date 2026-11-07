namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The description of an AV1 stream of an AVIF file for the container: what its sequence header declares.</summary>
internal sealed class AvifStreamDescription : HeifStreamDescription
{
    public AvifStreamDescription(Av1SequenceHeader sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        Sequence = sequence;
    }

    /// <summary>Gets the sequence header of the stream, whose profile and level select the brands of the file.</summary>
    public Av1SequenceHeader Sequence { get; }

    public override int PlaneCount => Sequence.PlaneCount;

    public override int BitDepth => Sequence.BitDepth;

    // AV1 Codec ISO Media File Format Binding, section 2.3 "AV1 Codec Configuration Box": the record without
    // configuration OBU, since the sequence header is in the item
    public override void WriteConfigurationBox(HeifBoxWriter writer)
    {
        var sequence = Sequence;
        var point = sequence.GetOperatingPoint(0);
        var box = writer.BeginBox(HeifFormat.Av1C);
        writer.WriteByte(0x81); // marker and version
        writer.WriteByte((byte)((sequence.Profile << 5) | point.LevelIndex));
        writer.WriteByte((byte)((point.Tier << 7)
            | (sequence.BitDepth > 8 ? 0x40 : 0)
            | (sequence.BitDepth is 12 ? 0x20 : 0)
            | (sequence.Monochrome ? 0x10 : 0)
            | (sequence.SubsamplingX << 3)
            | (sequence.SubsamplingY << 2)
            | sequence.ChromaSamplePosition));
        writer.WriteByte(0); // no initial presentation delay
        writer.EndBox(box);
    }
}
