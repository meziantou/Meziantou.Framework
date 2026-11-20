namespace Meziantou.Framework.Mail.Internals.Ltp;

internal static class PropertyTypes
{
    /// <summary>Returns the size of a value of a fixed-size type, or 0 for a variable-size type.</summary>
    public static int GetFixedSize(PstPropertyType type) => type switch
    {
        PstPropertyType.Boolean => 1,
        PstPropertyType.Integer16 => 2,
        PstPropertyType.Integer32 or PstPropertyType.Floating32 or PstPropertyType.ErrorCode => 4,
        PstPropertyType.Integer64 or PstPropertyType.Floating64 or PstPropertyType.Currency or PstPropertyType.FloatingTime or PstPropertyType.Time => 8,
        PstPropertyType.ClassId => 16,
        _ => 0,
    };

    /// <summary>Gets whether a property context stores the value in its record (types of 4 bytes or less).</summary>
    public static bool IsStoredInPropertyContextRecord(PstPropertyType type) => GetFixedSize(type) is > 0 and <= 4;

    public static bool IsMultiValued(PstPropertyType type) => ((ushort)type & 0x1000) != 0;

    public static PstPropertyType GetElementType(PstPropertyType type) => (PstPropertyType)((ushort)type & ~0x1000);
}
