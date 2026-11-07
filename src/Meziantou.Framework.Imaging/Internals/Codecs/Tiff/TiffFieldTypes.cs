namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Helpers over <see cref="TiffFieldType"/>.</summary>
internal static class TiffFieldTypes
{
    /// <summary>Gets the size of one value of a type, in bytes, or 0 when the type is unknown.</summary>
    /// <param name="type">The field type.</param>
    /// <returns>The size, in bytes.</returns>
    public static int GetSize(TiffFieldType type) => type switch
    {
        TiffFieldType.Byte or TiffFieldType.Ascii or TiffFieldType.SByte or TiffFieldType.Undefined => 1,
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float or TiffFieldType.Ifd => 4,
        TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double or TiffFieldType.Long8 or TiffFieldType.SLong8 or TiffFieldType.Ifd8 => 8,
        _ => 0,
    };

    /// <summary>Determines whether a type holds unsigned integers that fit in a <see cref="ulong"/>.</summary>
    /// <param name="type">The field type.</param>
    /// <returns><see langword="true"/> for BYTE, SHORT, LONG, LONG8 and the IFD pointer types.</returns>
    public static bool IsUnsignedInteger(TiffFieldType type)
        => type is TiffFieldType.Byte or TiffFieldType.Short or TiffFieldType.Long or TiffFieldType.Long8 or TiffFieldType.Ifd or TiffFieldType.Ifd8;

    /// <summary>Gets the name of a type, for error messages.</summary>
    /// <param name="type">The field type.</param>
    /// <returns>The name.</returns>
    public static string GetName(TiffFieldType type)
        => Enum.IsDefined(type) && type != TiffFieldType.Unknown ? type.ToString().ToUpperInvariant() : string.Create(CultureInfo.InvariantCulture, $"type {(ushort)type}");
}
