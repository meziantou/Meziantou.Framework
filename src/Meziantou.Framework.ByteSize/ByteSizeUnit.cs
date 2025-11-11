namespace Meziantou.Framework;

/// <summary>Specifies the units for byte size values, including both decimal (1000-based) and binary (1024-based) units.</summary>
public enum ByteSizeUnit : long
{
    /// <summary>Byte (B) - 1 byte.</summary>
    Byte = 1L,

    /// <summary>Kilobyte (kB) - 1,000 bytes.</summary>
    KiloByte = 1_000L,

    /// <summary>Megabyte (MB) - 1,000,000 bytes.</summary>
    MegaByte = 1_000_000L,

    /// <summary>Gigabyte (GB) - 1,000,000,000 bytes.</summary>
    GigaByte = 1_000_000_000L,

    /// <summary>Terabyte (TB) - 1,000,000,000,000 bytes.</summary>
    TeraByte = 1_000_000_000_000L,

    /// <summary>Petabyte (PB) - 1,000,000,000,000,000 bytes.</summary>
    PetaByte = 1_000_000_000_000_000L,

    /// <summary>Exabyte (EB) - 1,000,000,000,000,000,000 bytes.</summary>
    ExaByte = 1_000_000_000_000_000_000L,

    /// <summary>Kibibyte (kiB) - 1,024 bytes (2^10).</summary>
    KibiByte = 1L << 10,

    /// <summary>Mebibyte (MiB) - 1,048,576 bytes (2^20).</summary>
    MebiByte = 1L << 20,

    /// <summary>Gibibyte (GiB) - 1,073,741,824 bytes (2^30).</summary>
    GibiByte = 1L << 30,

    /// <summary>Tebibyte (TiB) - 1,099,511,627,776 bytes (2^40).</summary>
    TebiByte = 1L << 40,

    /// <summary>Pebibyte (PiB) - 1,125,899,906,842,624 bytes (2^50).</summary>
    PebiByte = 1L << 50,

    /// <summary>Exbibyte (EiB) - 1,152,921,504,606,846,976 bytes (2^60).</summary>
    ExbiByte = 1L << 60,
}
