using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Contains parsed information from a BCrypt hash string.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct BcryptHashInfo : IEquatable<BcryptHashInfo>
{
    /// <summary>Initializes a new instance of the <see cref="BcryptHashInfo"/> struct.</summary>
    /// <param name="version">The BCrypt revision.</param>
    /// <param name="workFactor">The BCrypt work factor (cost).</param>
    public BcryptHashInfo(BcryptVersion version, int workFactor)
    {
        Version = version;
        WorkFactor = workFactor;
    }

    /// <summary>Gets the BCrypt revision.</summary>
    public BcryptVersion Version { get; }

    /// <summary>Gets the BCrypt work factor (cost).</summary>
    public int WorkFactor { get; }

    public override bool Equals(object? obj) => obj is BcryptHashInfo other && Equals(other);

    public bool Equals(BcryptHashInfo other) => Version == other.Version && WorkFactor == other.WorkFactor;

    public override int GetHashCode() => HashCode.Combine(Version, WorkFactor);

    public static bool operator ==(BcryptHashInfo left, BcryptHashInfo right) => left.Equals(right);

    public static bool operator !=(BcryptHashInfo left, BcryptHashInfo right) => !(left == right);
}