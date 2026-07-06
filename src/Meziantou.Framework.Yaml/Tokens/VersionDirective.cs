namespace Meziantou.Framework.Yaml.Tokens;

/// <summary>Represents a version directive token.</summary>
public class VersionDirective : Token
{
    /// <summary>Gets the version.</summary>
    /// <value>The version.</value>
    public Version Version { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionDirective"/> class.
    /// </summary>
    /// <param name="version">The version.</param>
    public VersionDirective(Version version)
        : this(version, Mark.Empty, Mark.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionDirective"/> class.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="start">The start position of the token.</param>
    /// <param name="end">The end position of the token.</param>
    public VersionDirective(Version version, Mark start, Mark end)
        : base(start, end)
    {
        Version = version;
    }

    /// <summary>Determines whether the specified System.Object is equal to the current System.Object.</summary>
    /// <param name="obj">The System.Object to compare with the current System.Object.</param>
    /// <returns>true if the specified System.Object is equal to the current System.Object; otherwise, false.</returns>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is VersionDirective other && Version.Equals(other.Version);

    /// <summary>Serves as a hash function for a particular type.</summary>
    /// <returns>
    /// A hash code for the current <see cref="object"/>.
    /// </returns>
    public override int GetHashCode() => Version.GetHashCode();
}