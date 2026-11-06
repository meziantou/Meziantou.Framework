namespace Meziantou.Framework.Geo;

/// <summary>The options of the GPX writer, <see cref="Gpx"/>.</summary>
public sealed class GpxWriterOptions
{
    private string _creator = "Meziantou.Framework.Geo";

    /// <summary>Gets or sets a value that indicates whether the XML is indented. The default is <see langword="false"/>.</summary>
    public bool Indented { get; set; }

    /// <summary>Gets or sets the <c>creator</c> attribute of the <c>gpx</c> element: the name of the application that writes the file. The default is <c>Meziantou.Framework.Geo</c>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public string Creator
    {
        get => _creator;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _creator = value;
        }
    }
}
