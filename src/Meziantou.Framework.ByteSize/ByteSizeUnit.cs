namespace Meziantou.Framework;

/// <summary>
/// Specifies the units for byte size values.
/// </summary>
public enum ByteSizeUnit : long
{
    /// <summary>
    /// Represents bytes (1 byte).
    /// </summary>
    Byte = 1L,

    /// <summary>
    /// Represents kilobytes (1,000 bytes).
    /// </summary>
    KiloByte = 1_000L,

    /// <summary>
    /// Represents megabytes (1,000,000 bytes).
    /// </summary>
    MegaByte = 1_000_000L,

    /// <summary>
    /// Represents gigabytes (1,000,000,000 bytes).
    /// </summary>
    GigaByte = 1_000_000_000L,

    /// <summary>
    /// Represents terabytes (1,000,000,000,000 bytes).
    /// </summary>
    TeraByte = 1_000_000_000_000L,

    /// <summary>
    /// Represents petabytes (1,000,000,000,000,000 bytes).
    /// </summary>
    PetaByte = 1_000_000_000_000_000L,

    /// <summary>
    /// Represents exabytes (1,000,000,000,000,000,000 bytes).
    /// </summary>
    ExaByte = 1_000_000_000_000_000_000L,

    /// <summary>
    /// Represents kibibytes (1,024 bytes).
    /// </summary>
    KibiByte = 1L << 10,

    /// <summary>
    /// Represents mebibytes (1,048,576 bytes).
    /// </summary>
    MebiByte = 1L << 20,

    /// <summary>
    /// Represents gibibytes (1,073,741,824 bytes).
    /// </summary>
    GibiByte = 1L << 30,

    /// <summary>
    /// Represents tebibytes (1,099,511,627,776 bytes).
    /// </summary>
    TebiByte = 1L << 40,

    /// <summary>
    /// Represents pebibytes (1,125,899,906,842,624 bytes).
    /// </summary>
    PebiByte = 1L << 50,

    /// <summary>
    /// Represents exbibytes (1,152,921,504,606,846,976 bytes).
    /// </summary>
    ExbiByte = 1L << 60,
}
