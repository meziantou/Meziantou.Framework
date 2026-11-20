using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>An appointment (IPM.Appointment) to add to a folder of a <see cref="PstWriter"/>.</summary>
public sealed class PstAppointmentBuilder : PstMessageBuilder
{
    /// <summary>Initializes a new instance of the <see cref="PstAppointmentBuilder"/> class.</summary>
    public PstAppointmentBuilder()
        : base("IPM.Appointment")
    {
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
    public PstBusyStatus BusyStatus
    {
        get => Properties.Get(NamedProperties.BusyStatus.Name) is int value ? (PstBusyStatus)value : PstBusyStatus.Busy;
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
    public PstRecurrencePattern? Recurrence { get; set; }

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
                PstRecurrenceFrequency.Daily => 1,
                PstRecurrenceFrequency.Weekly => 2,
                PstRecurrenceFrequency.Monthly => 3,
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
            BusyStatus = PstBusyStatus.Busy;
        }
    }
}
