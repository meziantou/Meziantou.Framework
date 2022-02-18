namespace Meziantou.Framework;

public enum ByteSizeUnit : long
{
    Byte = 1L,
    KiloByte = 1_000L,
    MegaByte = 1_000_000L,
    GigaByte = 1_000_000_000L,
    TeraByte = 1_000_000_000_000L,
    PetaByte = 1_000_000_000_000_000L,
    ExaByte = 1_000_000_000_000_000_000L,

    KibiByte = 1L << 10,
    MebiByte = 1L << 20,
    GibiByte = 1L << 30,
    TebiByte = 1L << 40,
    PebiByte = 1L << 50,
    ExbiByte = 1L << 60,
}
