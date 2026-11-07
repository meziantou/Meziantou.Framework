using System.Runtime.InteropServices;

namespace Meziantou.Framework.Hashing;

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

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BcryptHashInfo other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(BcryptHashInfo other) => Version == other.Version && WorkFactor == other.WorkFactor;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Version, WorkFactor);

    /// <summary>Determines whether two hash information values are equal.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(BcryptHashInfo left, BcryptHashInfo right) => left.Equals(right);

    /// <summary>Determines whether two hash information values are different.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> if the values are different; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(BcryptHashInfo left, BcryptHashInfo right) => !(left == right);
}