

//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     GitVersion
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Meziantou.Framework
{
    partial struct ByteSize
    {

        public static ByteSize FromBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.Byte);
        public static ByteSize FromBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.Byte);
        public static ByteSize FromBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.Byte);
        public static ByteSize FromBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.Byte);
        public static ByteSize FromBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.Byte));
        public static ByteSize FromBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.Byte));

        public static ByteSize FromKiloBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.KiloByte);
        public static ByteSize FromKiloBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.KiloByte);
        public static ByteSize FromKiloBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.KiloByte);
        public static ByteSize FromKiloBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.KiloByte);
        public static ByteSize FromKiloBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.KiloByte));
        public static ByteSize FromKiloBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.KiloByte));

        public static ByteSize FromMegaBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.MegaByte);
        public static ByteSize FromMegaBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.MegaByte);
        public static ByteSize FromMegaBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.MegaByte);
        public static ByteSize FromMegaBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.MegaByte);
        public static ByteSize FromMegaBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.MegaByte));
        public static ByteSize FromMegaBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.MegaByte));

        public static ByteSize FromGigaBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.GigaByte);
        public static ByteSize FromGigaBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.GigaByte);
        public static ByteSize FromGigaBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.GigaByte);
        public static ByteSize FromGigaBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.GigaByte);
        public static ByteSize FromGigaBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.GigaByte));
        public static ByteSize FromGigaBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.GigaByte));

        public static ByteSize FromTeraBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.TeraByte);
        public static ByteSize FromTeraBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.TeraByte);
        public static ByteSize FromTeraBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.TeraByte);
        public static ByteSize FromTeraBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.TeraByte);
        public static ByteSize FromTeraBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.TeraByte));
        public static ByteSize FromTeraBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.TeraByte));

        public static ByteSize FromPetaBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.PetaByte);
        public static ByteSize FromPetaBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.PetaByte);
        public static ByteSize FromPetaBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.PetaByte);
        public static ByteSize FromPetaBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.PetaByte);
        public static ByteSize FromPetaBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.PetaByte));
        public static ByteSize FromPetaBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.PetaByte));

        public static ByteSize FromExaBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.ExaByte);
        public static ByteSize FromExaBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.ExaByte);
        public static ByteSize FromExaBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.ExaByte);
        public static ByteSize FromExaBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.ExaByte);
        public static ByteSize FromExaBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.ExaByte));
        public static ByteSize FromExaBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.ExaByte));

        public static ByteSize FromKibiBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.KibiByte);
        public static ByteSize FromKibiBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.KibiByte);
        public static ByteSize FromKibiBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.KibiByte);
        public static ByteSize FromKibiBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.KibiByte);
        public static ByteSize FromKibiBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.KibiByte));
        public static ByteSize FromKibiBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.KibiByte));

        public static ByteSize FromMebiBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.MebiByte);
        public static ByteSize FromMebiBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.MebiByte);
        public static ByteSize FromMebiBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.MebiByte);
        public static ByteSize FromMebiBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.MebiByte);
        public static ByteSize FromMebiBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.MebiByte));
        public static ByteSize FromMebiBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.MebiByte));

        public static ByteSize FromGibiBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.GibiByte);
        public static ByteSize FromGibiBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.GibiByte);
        public static ByteSize FromGibiBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.GibiByte);
        public static ByteSize FromGibiBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.GibiByte);
        public static ByteSize FromGibiBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.GibiByte));
        public static ByteSize FromGibiBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.GibiByte));

        public static ByteSize FromTebiBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.TebiByte);
        public static ByteSize FromTebiBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.TebiByte);
        public static ByteSize FromTebiBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.TebiByte);
        public static ByteSize FromTebiBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.TebiByte);
        public static ByteSize FromTebiBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.TebiByte));
        public static ByteSize FromTebiBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.TebiByte));

        public static ByteSize FromPebiBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.PebiByte);
        public static ByteSize FromPebiBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.PebiByte);
        public static ByteSize FromPebiBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.PebiByte);
        public static ByteSize FromPebiBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.PebiByte);
        public static ByteSize FromPebiBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.PebiByte));
        public static ByteSize FromPebiBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.PebiByte));

        public static ByteSize FromExbiBytes(byte value) => new ByteSize((long)value * (long)ByteSizeUnit.ExbiByte);
        public static ByteSize FromExbiBytes(short value) => new ByteSize((long)value * (long)ByteSizeUnit.ExbiByte);
        public static ByteSize FromExbiBytes(int value) => new ByteSize((long)value * (long)ByteSizeUnit.ExbiByte);
        public static ByteSize FromExbiBytes(long value) => new ByteSize((long)value * (long)ByteSizeUnit.ExbiByte);
        public static ByteSize FromExbiBytes(float value) => new ByteSize((long)(value * (long)ByteSizeUnit.ExbiByte));
        public static ByteSize FromExbiBytes(double value) => new ByteSize((long)(value * (long)ByteSizeUnit.ExbiByte));

    }
}
