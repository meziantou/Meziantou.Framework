using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Usn : IEquatable<Usn>
{
    public static Usn Zero => new(0);

    public long Value { get; }

    public Usn(long value) => Value = value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public override bool Equals(object? obj) => obj is Usn usn && Equals(usn);

    public bool Equals(Usn other) => Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(Usn usn1, Usn usn2) => usn1.Equals(usn2);

    public static bool operator !=(Usn usn1, Usn usn2) => !(usn1 == usn2);

    public static bool operator <=(Usn usn1, Usn usn2) => usn1.Value <= usn2.Value;

    public static bool operator >=(Usn usn1, Usn usn2) => usn1.Value >= usn2.Value;

    public static bool operator <(Usn usn1, Usn usn2) => usn1.Value < usn2.Value;

    public static bool operator >(Usn usn1, Usn usn2) => usn1.Value > usn2.Value;

    public static implicit operator Usn(long value) => new(value);
}
