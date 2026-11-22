namespace Meziantou.Framework.Email.Internals.Ltp;

internal static class PropertyTypes
{
    /// <summary>Returns the size of a value of a fixed-size type, or 0 for a variable-size type.</summary>
    public static int GetFixedSize(MapiPropertyType type) => type switch
    {
        MapiPropertyType.Boolean => 1,
        MapiPropertyType.Integer16 => 2,
        MapiPropertyType.Integer32 or MapiPropertyType.Floating32 or MapiPropertyType.ErrorCode => 4,
        MapiPropertyType.Integer64 or MapiPropertyType.Floating64 or MapiPropertyType.Currency or MapiPropertyType.FloatingTime or MapiPropertyType.Time => 8,
        MapiPropertyType.ClassId => 16,
        _ => 0,
    };

    /// <summary>Gets whether a property context stores the value in its record (types of 4 bytes or less).</summary>
    public static bool IsStoredInPropertyContextRecord(MapiPropertyType type) => GetFixedSize(type) is > 0 and <= 4;

    public static bool IsMultiValued(MapiPropertyType type) => ((ushort)type & 0x1000) != 0;

    public static MapiPropertyType GetElementType(MapiPropertyType type) => (MapiPropertyType)((ushort)type & ~0x1000);
}
