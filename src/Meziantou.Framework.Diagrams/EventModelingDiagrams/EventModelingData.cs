namespace Meziantou.Framework.Diagrams;

/// <summary>The data of a frame, a data block or a note of an event model: an example or a schema, as text.</summary>
public sealed class EventModelingData
{
    /// <summary>Initializes a new instance of the <see cref="EventModelingData"/> class.</summary>
    /// <param name="content">The text of the data.</param>
    /// <param name="format">The format of the text, or <see langword="null"/> when it is not given.</param>
    public EventModelingData(string content, EventModelingDataFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        Format = format;
    }

    /// <summary>Gets or sets the text of the data. The inline data of a frame is on one line; a data block can have several lines.</summary>
    public string Content { get; set; }

    /// <summary>Gets or sets the format of the text, or <see langword="null"/> when it is not given. It is not used to draw the data.</summary>
    public EventModelingDataFormat? Format { get; set; }
}
