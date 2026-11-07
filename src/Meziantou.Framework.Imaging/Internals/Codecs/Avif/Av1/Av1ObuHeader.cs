namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The header of an open bitstream unit (AV1 specification, sections 5.3.1 to 5.3.3) and the extent of its payload.
/// </summary>
/// <param name="Type">The <c>obu_type</c>, one of the <c>Obu*</c> values of <see cref="Av1Constants"/> or a reserved value.</param>
/// <param name="HasExtension">Whether the header carries a temporal and a spatial layer identifier (<c>obu_extension_flag</c>).</param>
/// <param name="TemporalId">The temporal layer of the unit, 0 without extension.</param>
/// <param name="SpatialId">The spatial layer of the unit, 0 without extension.</param>
/// <param name="HeaderLength">The number of bytes before the payload: the header, its extension and the size field.</param>
/// <param name="PayloadLength">The number of payload bytes (<c>obu_size</c>).</param>
internal readonly record struct Av1ObuHeader(int Type, bool HasExtension, int TemporalId, int SpatialId, int HeaderLength, int PayloadLength)
{
    /// <summary>The highest value of <c>spatial_id</c>, which has 2 bits.</summary>
    public const int MaxSpatialId = 3;

    /// <summary>Gets the number of bytes of the whole unit.</summary>
    public int TotalLength => HeaderLength + PayloadLength;

    /// <summary>
    /// Reads the header of the unit that starts <paramref name="data"/>. A unit without a size field extends to the end of
    /// <paramref name="data"/>: ISOBMFF samples and configuration records may omit the size of their last unit.
    /// </summary>
    /// <exception cref="InvalidImageContentException">The header is malformed, or the unit is longer than <paramref name="data"/>.</exception>
    public static Av1ObuHeader Read(ReadOnlySpan<byte> data)
    {
        var reader = new Av1BitReader(data);
        if (reader.ReadFlag())
            throw Av1Errors.Invalid("the forbidden bit of an OBU header is set.");

        var type = (int)reader.ReadBits(4);
        var hasExtension = reader.ReadFlag();
        var hasSizeField = reader.ReadFlag();
        reader.ReadBits(1); // obu_reserved_1bit: ignored by decoders

        var temporalId = 0;
        var spatialId = 0;
        if (hasExtension)
        {
            temporalId = (int)reader.ReadBits(3);
            spatialId = (int)reader.ReadBits(2);
            reader.ReadBits(3); // extension_header_reserved_3bits: ignored by decoders
        }

        long payloadLength;
        if (hasSizeField)
        {
            payloadLength = reader.ReadLeb128();
        }
        else
        {
            payloadLength = data.Length - reader.BytePosition;
        }

        var headerLength = reader.BytePosition;
        if (payloadLength > data.Length - headerLength)
            throw Av1Errors.Truncated();

        return new Av1ObuHeader(type, hasExtension, temporalId, spatialId, headerLength, (int)payloadLength);
    }

    /// <summary>
    /// Returns whether the unit is dropped because its layers are not part of the selected operating point (section 5.3.1),
    /// or because its spatial layer is above <paramref name="maxSpatialLayer"/>: sequence headers and temporal delimiters
    /// are never dropped.
    /// </summary>
    public bool IsOutsideOperatingPoint(int operatingPointIdc, int maxSpatialLayer = MaxSpatialId)
    {
        if (Type is Av1Constants.ObuSequenceHeader or Av1Constants.ObuTemporalDelimiter || operatingPointIdc is 0 || !HasExtension)
            return false;

        var inTemporalLayer = ((operatingPointIdc >> TemporalId) & 1) is not 0;
        var inSpatialLayer = ((operatingPointIdc >> (SpatialId + 8)) & 1) is not 0;
        return !inTemporalLayer || !inSpatialLayer || SpatialId > maxSpatialLayer;
    }
}
