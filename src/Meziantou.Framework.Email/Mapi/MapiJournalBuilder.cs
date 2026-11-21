using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>A journal entry (IPM.Activity, [MS-OXOJRNL]) to add to a folder of a <see cref="PstWriter"/> or to save as an .msg file.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Email.Tests/PstWriterTests.cs" ref:region="MapiJournalBuilder_Usage" ref:dedent="true">
/// var entry = new MapiJournalBuilder
/// {
///     Subject = "Call with Bob",
///     JournalType = "Phone call",
///     JournalTypeDescription = "Phone call",
///     Start = new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
///     End = new DateTime(2026, 3, 2, 9, 25, 0, DateTimeKind.Utc),
///     Companies = ["Contoso"],
///     Contacts = ["Bob Smith"],
/// };
/// Assert.Equal(TimeSpan.FromMinutes(25), entry.Duration);
///
/// var item = Assert.IsType&lt;MapiJournal&gt;(MsgFile.Load(MsgFile.ToByteArray(entry)));
/// Assert.Equal("Phone call", item.JournalType);
/// Assert.Equal(TimeSpan.FromMinutes(25), item.Duration);
/// Assert.Equal(["Contoso"], item.Companies);
/// </code>
/// </example>
public sealed class MapiJournalBuilder : MapiMessageBuilder
{
    /// <summary>Initializes a new instance of the <see cref="MapiJournalBuilder"/> class.</summary>
    public MapiJournalBuilder()
        : base("IPM.Activity")
    {
    }

    /// <summary>Gets or sets the type of the activity, such as <c>Phone call</c>, <c>Meeting</c> or <c>Microsoft Word</c>.</summary>
    public string? JournalType
    {
        get => Properties.GetValue<string>(NamedProperties.LogType);
        set => Properties.SetValue(NamedProperties.LogType, value);
    }

    /// <summary>Gets or sets the description of the type of the activity, as Outlook shows it.</summary>
    public string? JournalTypeDescription
    {
        get => Properties.GetValue<string>(NamedProperties.LogTypeDescription);
        set => Properties.SetValue(NamedProperties.LogTypeDescription, value);
    }

    /// <summary>Gets or sets the date and time, in UTC, when the activity started. It sets the duration when the end is set.</summary>
    public DateTime? Start
    {
        get => Properties.Get(NamedProperties.LogStart.Name) as DateTime?;
        set
        {
            // [MS-OXOJRNL] 2.2.1.2: the start is PidLidCommonStart too
            Properties.SetValue(NamedProperties.LogStart, value);
            Properties.SetValue(NamedProperties.CommonStart, value);
            UpdateDuration();
        }
    }

    /// <summary>Gets or sets the date and time, in UTC, when the activity ended. It sets the duration when the start is set.</summary>
    public DateTime? End
    {
        get => Properties.Get(NamedProperties.LogEnd.Name) as DateTime?;
        set
        {
            Properties.SetValue(NamedProperties.LogEnd, value);
            Properties.SetValue(NamedProperties.CommonEnd, value);
            UpdateDuration();
        }
    }

    /// <summary>Gets the duration of the activity, the difference between its end and its start, in whole minutes.</summary>
    public TimeSpan? Duration => Properties.Get(NamedProperties.LogDuration.Name) is int minutes ? TimeSpan.FromMinutes(minutes) : null;

    /// <summary>Gets or sets the names of the companies the activity is associated with.</summary>
    public IReadOnlyList<string>? Companies
    {
        get => Properties.GetValue<string[]>(NamedProperties.Companies);
        set => Properties.SetValue(NamedProperties.Companies, value?.ToArray());
    }

    /// <summary>Gets or sets the names of the contacts the activity is associated with.</summary>
    public IReadOnlyList<string>? Contacts
    {
        get => Properties.GetValue<string[]>(NamedProperties.Contacts);
        set => Properties.SetValue(NamedProperties.Contacts, value?.ToArray());
    }

    /// <summary>Gets or sets whether the document was printed during the activity.</summary>
    public bool DocumentPrinted
    {
        get => Properties.Get(NamedProperties.LogDocumentPrinted.Name) is true;
        set => Properties.SetValue(NamedProperties.LogDocumentPrinted, value);
    }

    /// <summary>Gets or sets whether the document was saved during the activity.</summary>
    public bool DocumentSaved
    {
        get => Properties.Get(NamedProperties.LogDocumentSaved.Name) is true;
        set => Properties.SetValue(NamedProperties.LogDocumentSaved, value);
    }

    /// <summary>Gets or sets whether the document was sent to a routing recipient during the activity.</summary>
    public bool DocumentRouted
    {
        get => Properties.Get(NamedProperties.LogDocumentRouted.Name) is true;
        set => Properties.SetValue(NamedProperties.LogDocumentRouted, value);
    }

    /// <summary>Gets or sets whether the document was sent by email or posted to a folder during the activity.</summary>
    public bool DocumentPosted
    {
        get => Properties.Get(NamedProperties.LogDocumentPosted.Name) is true;
        set => Properties.SetValue(NamedProperties.LogDocumentPosted, value);
    }

    private void UpdateDuration()
    {
        // [MS-OXOJRNL] 2.2.1.5: the duration is the difference between the end and the start, in minutes
        if (Start is { } start && End is { } end && end >= start)
        {
            Properties.SetValue(NamedProperties.LogDuration, (int)Math.Min((end - start).TotalMinutes, int.MaxValue));
        }
        else
        {
            Properties.SetValue(NamedProperties.LogDuration, null);
        }
    }
}
