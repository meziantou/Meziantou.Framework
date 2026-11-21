using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>An appointment (IPM.Appointment) to add to a folder of a <see cref="PstWriter"/>.</summary>
public class MapiAppointmentBuilder : MapiMessageBuilder
{
    /// <summary>Initializes a new instance of the <see cref="MapiAppointmentBuilder"/> class.</summary>
    public MapiAppointmentBuilder()
        : base("IPM.Appointment")
    {
    }

    private protected MapiAppointmentBuilder(string messageClass)
        : base(messageClass)
    {
    }

    /// <summary>Gets or sets the unique identifier of the meeting, the <c>UID</c> of iCalendar. It is stored in the global object identifier of the item ([MS-OXCICAL] 2.1.3.1.1.20.26).</summary>
    public string? Uid
    {
        get => Properties.Get(NamedProperties.CleanGlobalObjectId.Name) is byte[] id ? GlobalObjectId.ToUid(id) : null;
        set
        {
            var id = value is null ? null : GlobalObjectId.FromUid(value);
            Properties.SetValue(NamedProperties.GlobalObjectId, id);
            Properties.SetValue(NamedProperties.CleanGlobalObjectId, id is null ? null : (byte[])id.Clone());
        }
    }

    /// <summary>Gets or sets the sequence number of the meeting, incremented by the organizer when the meeting changes.</summary>
    public int? Sequence
    {
        get => Properties.Get(NamedProperties.AppointmentSequence.Name) as int?;
        set => Properties.SetValue(NamedProperties.AppointmentSequence, value);
    }

    /// <summary>Gets or sets whether the appointment is a meeting, which has attendees: the recipients.</summary>
    public bool IsMeeting
    {
        get => Properties.Get(NamedProperties.AppointmentStateFlags.Name) is int flags && (flags & 0x1) != 0;
        set
        {
            var flags = Properties.Get(NamedProperties.AppointmentStateFlags.Name) as int? ?? 0;
            Properties.SetValue(NamedProperties.AppointmentStateFlags, value ? flags | 0x1 : flags & ~0x1);
        }
    }

    /// <summary>Gets or sets the date and time, in UTC, when the appointment (or its first occurrence) starts.</summary>
    public DateTime? Start
    {
        get => Properties.Get(NamedProperties.AppointmentStartWhole.Name) as DateTime?;
        set
        {
            Properties.SetValue(NamedProperties.AppointmentStartWhole, value);
            Properties.SetValue(NamedProperties.CommonStart, value);
        }
    }

    /// <summary>Gets or sets the date and time, in UTC, when the appointment (or its first occurrence) ends.</summary>
    public DateTime? End
    {
        get => Properties.Get(NamedProperties.AppointmentEndWhole.Name) as DateTime?;
        set
        {
            Properties.SetValue(NamedProperties.AppointmentEndWhole, value);
            Properties.SetValue(NamedProperties.CommonEnd, value);
        }
    }

    /// <summary>Gets or sets the location.</summary>
    public string? Location
    {
        get => Properties.GetValue<string>(NamedProperties.Location);
        set => Properties.SetValue(NamedProperties.Location, value);
    }

    /// <summary>Gets or sets whether the appointment is an all-day event.</summary>
    public bool IsAllDay
    {
        get => Properties.Get(NamedProperties.AppointmentSubType.Name) is true;
        set => Properties.SetValue(NamedProperties.AppointmentSubType, value);
    }

    /// <summary>Gets or sets how the appointment shows the time of the user.</summary>
    public MapiBusyStatus BusyStatus
    {
        get => Properties.Get(NamedProperties.BusyStatus.Name) is int value ? (MapiBusyStatus)value : MapiBusyStatus.Busy;
        set => Properties.SetValue(NamedProperties.BusyStatus, (int)value);
    }

    /// <summary>Gets or sets how long before the start a reminder fires, or <see langword="null"/> for no reminder.</summary>
    public TimeSpan? Reminder
    {
        get => Properties.Get(NamedProperties.ReminderSet.Name) is true && Properties.Get(NamedProperties.ReminderDelta.Name) is int minutes ? TimeSpan.FromMinutes(minutes) : null;
        set
        {
            Properties.SetValue(NamedProperties.ReminderSet, value is not null);
            Properties.SetValue(NamedProperties.ReminderDelta, value is null ? null : (int)value.Value.TotalMinutes);
        }
    }

    /// <summary>Gets or sets the recurrence of the appointment, or <see langword="null"/> for a single appointment.</summary>
    public MapiRecurrencePattern? Recurrence { get; set; }

    internal void Complete()
    {
        if (IsCopy && Recurrence is null)
        {
            return;
        }

        if (Recurrence is not null)
        {
            Properties.SetValue(NamedProperties.AppointmentRecur, Recurrence.ToByteArray());
            Properties.SetValue(NamedProperties.Recurring, true);
            Properties.SetValue(NamedProperties.RecurrenceType, Recurrence.Frequency switch
            {
                MapiRecurrenceFrequency.Daily => 1,
                MapiRecurrenceFrequency.Weekly => 2,
                MapiRecurrenceFrequency.Monthly => 3,
                _ => 4,
            });
        }
        else if (Properties.Get(NamedProperties.AppointmentRecur.Name) is null)
        {
            Properties.SetValue(NamedProperties.Recurring, false);
        }

        if (Start is DateTime start && End is DateTime end && Properties.Get(NamedProperties.AppointmentDuration.Name) is null)
        {
            Properties.SetValue(NamedProperties.AppointmentDuration, (int)(end - start).TotalMinutes);
        }

        if (Properties.Get(NamedProperties.BusyStatus.Name) is null)
        {
            BusyStatus = MapiBusyStatus.Busy;
        }
    }
}
