namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>A marker segment read by <see cref="ReferenceJpeg"/> (SOI, EOI and the entropy-coded data are not segments).</summary>
/// <param name="Marker">The marker code (the byte after <c>FF</c>).</param>
/// <param name="Payload">The segment payload (after the 2-byte length).</param>
public sealed record ReferenceJpegSegment(byte Marker, ReadOnlyMemory<byte> Payload)
{
    /// <summary>Gets the conventional name of the marker (<c>APP0</c>, <c>DQT</c>, <c>SOF0</c>, <c>COM</c>...).</summary>
    public string Name => Marker switch
    {
        >= 0xE0 and <= 0xEF => "APP" + (Marker - 0xE0).ToString(CultureInfo.InvariantCulture),
        0xC0 => "SOF0",
        0xC1 => "SOF1",
        0xC4 => "DHT",
        0xDB => "DQT",
        0xDD => "DRI",
        0xDA => "SOS",
        0xFE => "COM",
        _ => "0x" + Marker.ToString("X2", CultureInfo.InvariantCulture),
    };
}
