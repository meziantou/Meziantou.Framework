using System;

namespace Meziantou.Framework.Win32
{
    public readonly struct Usn : IEquatable<Usn>
    {
        public long Value { get; }

        public Usn(long value)
        {
            Value = value;
        }

        public override string ToString()
        {
            return base.ToString();
        }

        public override bool Equals(object obj)
        {
            return obj is Usn && Equals((Usn)obj);
        }

        public bool Equals(Usn other)
        {
            return Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public static bool operator ==(Usn usn1, Usn usn2)
        {
            return usn1.Equals(usn2);
        }

        public static bool operator !=(Usn usn1, Usn usn2)
        {
            return !(usn1 == usn2);
        }

        public static bool operator <=(Usn usn1, Usn usn2)
        {
            return usn1.Value <= usn2.Value;
        }

        public static bool operator >=(Usn usn1, Usn usn2)
        {
            return usn1.Value >= usn2.Value;
        }

        public static bool operator <(Usn usn1, Usn usn2)
        {
            return usn1.Value < usn2.Value;
        }

        public static bool operator >(Usn usn1, Usn usn2)
        {
            return usn1.Value > usn2.Value;
        }

        public static implicit operator Usn(long value)
        {
            return new Usn(value);
        }
    }
}
