namespace Meziantou.Framework;

/// <summary>
/// Describes an icon set: its name, author and license. Each icon set has its own license.
/// </summary>
public sealed class IconifyIconSetInfo
{
    /// <summary>Gets the display name of the icon set, such as <c>Material Design Icons</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the name of the author of the icon set.</summary>
    public string? AuthorName { get; init; }

    /// <summary>Gets the URL of the author or of the project.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The value is copied from the icon set as is, and may not be a valid URI")]
    public string? AuthorUrl { get; init; }

    /// <summary>Gets the title of the license, such as <c>Apache 2.0</c>.</summary>
    public string? LicenseTitle { get; init; }

    /// <summary>Gets the SPDX identifier of the license, such as <c>Apache-2.0</c>.</summary>
    public string? LicenseSpdx { get; init; }

    /// <summary>Gets the URL of the license.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The value is copied from the icon set as is, and may not be a valid URI")]
    public string? LicenseUrl { get; init; }

    /// <summary>Gets the number of icons in the icon set, as declared by the icon set. It excludes the aliases and the hidden icons.</summary>
    public int? Total { get; init; }

    /// <summary>Gets the version of the icon set.</summary>
    public string? Version { get; init; }

    /// <summary>Gets the category of the icon set, such as <c>General</c>.</summary>
    public string? Category { get; init; }

    /// <summary>Gets a value indicating whether the icons have hard-coded colors (<see langword="true"/>) or use <c>currentColor</c> (<see langword="false"/>).</summary>
    public bool? Palette { get; init; }
}
