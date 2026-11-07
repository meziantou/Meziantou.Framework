using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf;

/// <summary>A version of the PDF format, such as 1.7 or 2.0.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PdfVersion : IEquatable<PdfVersion>, IComparable<PdfVersion>
{
    /// <summary>Initializes a new instance of the <see cref="PdfVersion"/> struct.</summary>
    /// <param name="major">The major version, 1 or 2.</param>
    /// <param name="minor">The minor version, from 0 to 9.</param>
    public PdfVersion(int major, int minor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(major, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(major, 9);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minor, 9);
        Major = major;
        Minor = minor;
    }

    /// <summary>Gets PDF 1.4.</summary>
    public static PdfVersion Pdf14 { get; } = new(1, 4);

    /// <summary>Gets PDF 1.5, the first version with object streams and cross-reference streams.</summary>
    public static PdfVersion Pdf15 { get; } = new(1, 5);

    /// <summary>Gets PDF 1.6.</summary>
    public static PdfVersion Pdf16 { get; } = new(1, 6);

    /// <summary>Gets PDF 1.7 (ISO 32000-1).</summary>
    public static PdfVersion Pdf17 { get; } = new(1, 7);

    /// <summary>Gets PDF 2.0 (ISO 32000-2).</summary>
    public static PdfVersion Pdf20 { get; } = new(2, 0);

    /// <summary>Gets the major version.</summary>
    public int Major { get; }

    /// <summary>Gets the minor version.</summary>
    public int Minor { get; }

    /// <summary>Indicates whether two versions are equal.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when the versions are equal.</returns>
    public static bool operator ==(PdfVersion left, PdfVersion right) => left.Equals(right);

    /// <summary>Indicates whether two versions are different.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when the versions are different.</returns>
    public static bool operator !=(PdfVersion left, PdfVersion right) => !left.Equals(right);

    /// <summary>Indicates whether a version is lower than another one.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is lower.</returns>
    public static bool operator <(PdfVersion left, PdfVersion right) => left.CompareTo(right) < 0;

    /// <summary>Indicates whether a version is greater than another one.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is greater.</returns>
    public static bool operator >(PdfVersion left, PdfVersion right) => left.CompareTo(right) > 0;

    /// <summary>Indicates whether a version is lower than or equal to another one.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is lower or equal.</returns>
    public static bool operator <=(PdfVersion left, PdfVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Indicates whether a version is greater than or equal to another one.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is greater or equal.</returns>
    public static bool operator >=(PdfVersion left, PdfVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Parses a version written as in a PDF header, such as <c>1.7</c>.</summary>
    /// <param name="value">The text.</param>
    /// <param name="version">The version.</param>
    /// <returns><see langword="true"/> when the text is a version.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out PdfVersion version) => TryParse(value.AsSpan(), out version);

    internal static bool TryParse(ReadOnlySpan<char> value, out PdfVersion version)
    {
        version = default;
        if (value.Length != 3 || value[1] != '.' || value[0] is < '1' or > '9' || value[2] is < '0' or > '9')
            return false;

        version = new PdfVersion(value[0] - '0', value[2] - '0');
        return true;
    }

    /// <summary>Returns the greater of two versions.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns>The greater version.</returns>
    public static PdfVersion Max(PdfVersion left, PdfVersion right) => left >= right ? left : right;

    /// <inheritdoc/>
    public int CompareTo(PdfVersion other) => Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);

    /// <inheritdoc/>
    public bool Equals(PdfVersion other) => Major == other.Major && Minor == other.Minor;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PdfVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Major, Minor);

    /// <summary>Returns the version as a PDF header writes it, such as <c>1.7</c>.</summary>
    /// <returns>The version.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");
}
