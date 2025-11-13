using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

/// <summary>Represents a Update Sequence Number (USN), which is a 64-bit value that uniquely identifies a change journal record.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Usn : IEquatable<Usn>
{
    /// <summary>
    /// Gets a <see cref="Usn"/> value representing zero.
    /// </summary>
    public static Usn Zero => new(0);

    /// <summary>Gets the USN value.</summary>
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
