namespace Meziantou.Framework.Mail;

/// <summary>The type of a MAPI property, the low 16 bits of its tag.</summary>
public enum PstPropertyType : ushort
{
    /// <summary>No type.</summary>
    Unspecified = 0x0000,

    /// <summary>No value.</summary>
    Null = 0x0001,

    /// <summary>A 16-bit signed integer (<see cref="short"/>).</summary>
    Integer16 = 0x0002,

    /// <summary>A 32-bit signed integer (<see cref="int"/>).</summary>
    Integer32 = 0x0003,

    /// <summary>A 32-bit floating point number (<see cref="float"/>).</summary>
    Floating32 = 0x0004,

    /// <summary>A 64-bit floating point number (<see cref="double"/>).</summary>
    Floating64 = 0x0005,

    /// <summary>A currency amount, a 64-bit integer scaled by 10,000 (<see cref="decimal"/>).</summary>
    Currency = 0x0006,

    /// <summary>A date in the OLE automation format (<see cref="DateTime"/>).</summary>
    FloatingTime = 0x0007,

    /// <summary>A 32-bit error code (<see cref="int"/>).</summary>
    ErrorCode = 0x000A,

    /// <summary>A Boolean (<see cref="bool"/>).</summary>
    Boolean = 0x000B,

    /// <summary>An object, such as an embedded message.</summary>
    ObjectReference = 0x000D,

    /// <summary>A 64-bit signed integer (<see cref="long"/>).</summary>
    Integer64 = 0x0014,

    /// <summary>A string in the code page of the message (<see cref="string"/>).</summary>
    String8 = 0x001E,

    /// <summary>A UTF-16 string (<see cref="string"/>).</summary>
    Unicode = 0x001F,

    /// <summary>A date and time in UTC (<see cref="DateTime"/>).</summary>
    Time = 0x0040,

    /// <summary>A GUID (<see cref="System.Guid"/>).</summary>
    ClassId = 0x0048,

    /// <summary>A server identifier (<see cref="byte"/> array).</summary>
    ServerId = 0x00FB,

    /// <summary>A restriction (<see cref="byte"/> array).</summary>
    Restriction = 0x00FD,

    /// <summary>A rule action (<see cref="byte"/> array).</summary>
    RuleAction = 0x00FE,

    /// <summary>Bytes (<see cref="byte"/> array).</summary>
    Binary = 0x0102,

    /// <summary>An array of 16-bit integers.</summary>
    MultipleInteger16 = 0x1002,

    /// <summary>An array of 32-bit integers.</summary>
    MultipleInteger32 = 0x1003,

    /// <summary>An array of 32-bit floating point numbers.</summary>
    MultipleFloating32 = 0x1004,

    /// <summary>An array of 64-bit floating point numbers.</summary>
    MultipleFloating64 = 0x1005,

    /// <summary>An array of currency amounts.</summary>
    MultipleCurrency = 0x1006,

    /// <summary>An array of OLE automation dates.</summary>
    MultipleFloatingTime = 0x1007,

    /// <summary>An array of 64-bit integers.</summary>
    MultipleInteger64 = 0x1014,

    /// <summary>An array of strings in the code page of the message.</summary>
    MultipleString8 = 0x101E,

    /// <summary>An array of UTF-16 strings.</summary>
    MultipleUnicode = 0x101F,

    /// <summary>An array of dates and times.</summary>
    MultipleTime = 0x1040,

    /// <summary>An array of GUIDs.</summary>
    MultipleClassId = 0x1048,

    /// <summary>An array of byte arrays.</summary>
    MultipleBinary = 0x1102,
}
