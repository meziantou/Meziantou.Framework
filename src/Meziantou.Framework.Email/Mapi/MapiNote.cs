using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>A sticky note (IPM.StickyNote, [MS-OXONOTE]). Its text is the <see cref="MapiMessage.Body"/>, and its subject is the first line of the text.</summary>
public sealed class MapiNote : MapiMessage
{
    internal MapiNote(MessageSource source, MapiPropertyCollection properties, int embeddingDepth)
        : base(source, properties, embeddingDepth)
    {
    }

    /// <summary>Gets the background color of the note.</summary>
    public MapiNoteColor Color => (MapiNoteColor)(Properties.GetInt32(NamedProperties.NoteColor.Name) ?? (int)MapiNoteColor.Yellow);

    /// <summary>Gets the width of the window of the note, in pixels.</summary>
    public int? Width => Properties.GetInt32(NamedProperties.NoteWidth.Name);

    /// <summary>Gets the height of the window of the note, in pixels.</summary>
    public int? Height => Properties.GetInt32(NamedProperties.NoteHeight.Name);

    /// <summary>Gets the distance, in pixels, from the left edge of the screen to the window of the note.</summary>
    public int? Left => Properties.GetInt32(NamedProperties.NoteX.Name);

    /// <summary>Gets the distance, in pixels, from the top edge of the screen to the window of the note.</summary>
    public int? Top => Properties.GetInt32(NamedProperties.NoteY.Name);
}
