using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>A sticky note (IPM.StickyNote, [MS-OXONOTE]) to add to a folder of a <see cref="PstWriter"/> or to save as an .msg file.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Email.Tests/PstWriterTests.cs" ref:region="MapiNoteBuilder_Usage" ref:dedent="true">
/// using (var writer = PstWriter.Create(stream, leaveOpen: true))
/// {
///     var notes = writer.RootFolder.CreateFolder("Notes", "IPF.StickyNote");
///     notes.AddItem(new MapiNoteBuilder
///     {
///         Text = "Call Bob\r\nAbout the quarterly report",
///         Color = MapiNoteColor.Green,
///         Width = 200,
///         Height = 166,
///     });
/// }
///
/// using var file = PstFile.Open(stream);
/// var note = Assert.IsType&lt;MapiNote&gt;(file.GetFolder("Notes")!.EnumerateItems().Single());
/// Assert.Equal("Call Bob", note.Subject);
/// Assert.Equal("Call Bob\r\nAbout the quarterly report", note.Body);
/// Assert.Equal(MapiNoteColor.Green, note.Color);
/// Assert.Equal(200, note.Width);
/// </code>
/// </example>
public sealed class MapiNoteBuilder : MapiMessageBuilder
{
    /// <summary>Initializes a new instance of the <see cref="MapiNoteBuilder"/> class.</summary>
    public MapiNoteBuilder()
        : base("IPM.StickyNote")
    {
    }

    /// <summary>Gets or sets the text of the note. It sets the subject too: Outlook shows the first line of the text as the subject ([MS-OXONOTE] 2.2.1.4).</summary>
    public string? Text
    {
        get => Body;
        set
        {
            Body = value;
            Subject = value is null ? null : GetFirstLine(value);
        }
    }

    /// <summary>Gets or sets the background color of the note.</summary>
    public MapiNoteColor Color
    {
        get => Properties.Get(NamedProperties.NoteColor.Name) is int value ? (MapiNoteColor)value : MapiNoteColor.Yellow;
        set => Properties.SetValue(NamedProperties.NoteColor, (int)value);
    }

    /// <summary>Gets or sets the width of the window of the note, in pixels.</summary>
    public int? Width
    {
        get => Properties.Get(NamedProperties.NoteWidth.Name) as int?;
        set => Properties.SetValue(NamedProperties.NoteWidth, value);
    }

    /// <summary>Gets or sets the height of the window of the note, in pixels.</summary>
    public int? Height
    {
        get => Properties.Get(NamedProperties.NoteHeight.Name) as int?;
        set => Properties.SetValue(NamedProperties.NoteHeight, value);
    }

    /// <summary>Gets or sets the distance, in pixels, from the left edge of the screen to the window of the note.</summary>
    public int? Left
    {
        get => Properties.Get(NamedProperties.NoteX.Name) as int?;
        set => Properties.SetValue(NamedProperties.NoteX, value);
    }

    /// <summary>Gets or sets the distance, in pixels, from the top edge of the screen to the window of the note.</summary>
    public int? Top
    {
        get => Properties.Get(NamedProperties.NoteY.Name) as int?;
        set => Properties.SetValue(NamedProperties.NoteY, value);
    }

    private static string GetFirstLine(string text)
    {
        var end = text.AsSpan().IndexOfAny('\r', '\n');
        return end < 0 ? text : text[..end];
    }
}
