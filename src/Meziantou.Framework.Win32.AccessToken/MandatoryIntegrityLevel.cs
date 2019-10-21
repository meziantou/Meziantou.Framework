#nullable disable
namespace Meziantou.Framework.Win32
{
    public enum MandatoryIntegrityLevel
    {
        Untrusted = 0x00000000,
        LowIntegrity = 0x00001000,
        MediumIntegrity = 0x00002000,
        MediumHighIntegrity = MediumIntegrity + 0x100,
        HighIntegrity = 0X00003000,
        SystemIntegrity = 0x00004000,
        ProtectedProcess = 0x00005000,
    }
}
