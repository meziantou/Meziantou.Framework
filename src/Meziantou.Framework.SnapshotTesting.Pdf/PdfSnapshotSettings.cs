namespace Meziantou.Framework.SnapshotTesting.Pdf;

/// <summary>Configures how a PDF document is converted to snapshot files: the rendering of its pages and the content of its metadata file.</summary>
public sealed class PdfSnapshotSettings
{
    private double _dpi = 72;

    /// <summary>
    /// Gets or sets the resolution the pages are rendered at, in dots per inch. The default, <c>72</c>, renders one pixel per
    /// point: an A4 page is 595×842 pixels.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite positive number.</exception>
    public double Dpi
    {
        get => _dpi;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The resolution must be a finite positive number.");

            _dpi = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether the appearances of the annotations are drawn on the pages. The default is <see langword="true"/>.</summary>
    public bool RenderAnnotations { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the fonts of the system replace the fonts the document does not embed. The
    /// default, <see langword="false"/>, uses the fonts bundled with <c>Meziantou.Framework.Pdf</c>, so the pages render the
    /// same on every machine.
    /// </summary>
    public bool UseSystemFonts { get; set; }

    /// <summary>Gets or sets the password of an encrypted document: its user or owner password.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets the optional parts of the JSON metadata file. The default is <see cref="PdfSnapshotMetadata.Default"/>.</summary>
    public PdfSnapshotMetadata Metadata { get; set; } = PdfSnapshotMetadata.Default;
}
