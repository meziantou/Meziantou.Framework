namespace Meziantou.Framework.Geo;

/// <summary>The options of the FlatGeobuf writer, <see cref="FlatGeobuf"/>.</summary>
public sealed class FlatGeobufWriterOptions
{
    /// <summary>
    /// Gets or sets a value that indicates whether the file gets a spatial index, which lets a reader get the features of a
    /// box without decoding the others. The features are then written in the order of the index. The default is <see langword="true"/>.
    /// </summary>
    public bool IncludeSpatialIndex { get; set; } = true;

    /// <summary>Gets or sets the number of children of each node of the spatial index, from 2 to 65,535. The default is 16.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 2.</exception>
    public ushort IndexNodeSize
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, (ushort)2);
            field = value;
        }
    } = 16;

    /// <summary>Gets or sets the name of the dataset, or <see langword="null"/> for the name of the collection. The default is <see langword="null"/>.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the title of the dataset, or <see langword="null"/> for none. The default is <see langword="null"/>.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the description of the dataset, or <see langword="null"/> for none. The default is <see langword="null"/>.</summary>
    public string? Description { get; set; }
}
