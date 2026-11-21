using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>A journal entry (IPM.Activity, [MS-OXOJRNL]): a record of an activity, such as a phone call or the editing of a document.</summary>
public sealed class MapiJournal : MapiMessage
{
    internal MapiJournal(MessageSource source, MapiPropertyCollection properties, int embeddingDepth)
        : base(source, properties, embeddingDepth)
    {
    }

    /// <summary>Gets the type of the activity, such as <c>Phone call</c>, <c>Meeting</c> or <c>Microsoft Word</c>.</summary>
    public string? JournalType => Properties.GetString(NamedProperties.LogType.Name);

    /// <summary>Gets the description of the type of the activity, as Outlook shows it.</summary>
    public string? JournalTypeDescription => Properties.GetString(NamedProperties.LogTypeDescription.Name);

    /// <summary>Gets the date and time, in UTC, when the activity started.</summary>
    public DateTime? Start => Properties.GetDateTime(NamedProperties.LogStart.Name);

    /// <summary>Gets the date and time, in UTC, when the activity ended.</summary>
    public DateTime? End => Properties.GetDateTime(NamedProperties.LogEnd.Name);

    /// <summary>Gets the duration of the activity.</summary>
    public TimeSpan? Duration => Properties.GetInt32(NamedProperties.LogDuration.Name) is int minutes ? TimeSpan.FromMinutes(minutes) : null;

    /// <summary>Gets the names of the companies the activity is associated with.</summary>
    public IReadOnlyList<string> Companies => Properties.GetValueOrDefault<string[]>(NamedProperties.Companies.Name) ?? [];

    /// <summary>Gets the names of the contacts the activity is associated with.</summary>
    public IReadOnlyList<string> Contacts => Properties.GetValueOrDefault<string[]>(NamedProperties.Contacts.Name) ?? [];

    /// <summary>Gets whether the document was printed during the activity.</summary>
    public bool DocumentPrinted => Properties.GetBoolean(NamedProperties.LogDocumentPrinted.Name) ?? false;

    /// <summary>Gets whether the document was saved during the activity.</summary>
    public bool DocumentSaved => Properties.GetBoolean(NamedProperties.LogDocumentSaved.Name) ?? false;

    /// <summary>Gets whether the document was sent to a routing recipient during the activity.</summary>
    public bool DocumentRouted => Properties.GetBoolean(NamedProperties.LogDocumentRouted.Name) ?? false;

    /// <summary>Gets whether the document was sent by email or posted to a folder during the activity.</summary>
    public bool DocumentPosted => Properties.GetBoolean(NamedProperties.LogDocumentPosted.Name) ?? false;
}
