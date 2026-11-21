using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail;

/// <summary>An appointment or a meeting (IPM.Appointment).</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Mail.Tests/PstFileTests.cs" ref:region="PstAppointment_Read" ref:dedent="true">
/// using var file = PstFile.Open(path);
/// var appointment = file.GetFolder("Calendar")!.EnumerateItems().OfType&lt;PstAppointment&gt;().Single();
/// Assert.Equal("Test appointment", appointment.Subject);
/// Assert.Equal(new DateTime(2016, 8, 2, 15, 0, 0, DateTimeKind.Utc), appointment.Start);
///
/// // The recurrence: every Tuesday, with deleted and moved occurrences
/// var recurrence = appointment.GetRecurrence()!;
/// Assert.Equal(PstRecurrenceFrequency.Weekly, recurrence.Frequency);
/// Assert.Equal(PstDaysOfWeek.Tuesday, recurrence.DaysOfWeek);
/// Assert.HasCount(3, recurrence.DeletedInstanceDates);
/// Assert.HasCount(2, recurrence.Exceptions);
/// Assert.Equal(new DateTime(2016, 8, 23, 9, 0, 0), recurrence.Exceptions[0].StartDateTime);
///
/// // The RTF body, decompressed from the compressed RTF property
/// var rtf = Encoding.ASCII.GetString(appointment.GetRtfBody()!);
/// Assert.StartsWith(@"{\rtf1", rtf, StringComparison.Ordinal);
/// </code>
/// </example>
public sealed class PstAppointment : PstMessage
{
    internal PstAppointment(MessageSource source, PstPropertyCollection properties, int embeddingDepth)
        : base(source, properties, embeddingDepth)
    {
    }

    /// <summary>Gets the date and time, in UTC, when the appointment (or the first occurrence of a recurring appointment) starts.</summary>
    public DateTime? Start => Properties.GetDateTime(NamedProperties.AppointmentStartWhole.Name);

    /// <summary>Gets the date and time, in UTC, when the appointment (or the first occurrence of a recurring appointment) ends.</summary>
    public DateTime? End => Properties.GetDateTime(NamedProperties.AppointmentEndWhole.Name);

    /// <summary>Gets the duration of the appointment.</summary>
    public TimeSpan? Duration => Properties.GetInt32(NamedProperties.AppointmentDuration.Name) is int minutes ? TimeSpan.FromMinutes(minutes) : End - Start;

    /// <summary>Gets the location.</summary>
    public string? Location => Properties.GetString(NamedProperties.Location.Name);

    /// <summary>Gets whether the appointment is an all-day event.</summary>
    public bool IsAllDay => Properties.GetBoolean(NamedProperties.AppointmentSubType.Name) ?? false;

    /// <summary>Gets how the appointment shows the time of the user.</summary>
    public PstBusyStatus BusyStatus => (PstBusyStatus)(Properties.GetInt32(NamedProperties.BusyStatus.Name) ?? (int)PstBusyStatus.Busy);

    /// <summary>Gets whether the appointment is a meeting, which has attendees.</summary>
    public bool IsMeeting => ((Properties.GetInt32(NamedProperties.AppointmentStateFlags.Name) ?? 0) & 0x1) != 0;

    /// <summary>Gets the display names of all the attendees, separated by semicolons.</summary>
    public string? AllAttendees => Properties.GetString(NamedProperties.AllAttendeesString.Name);

    /// <summary>Gets the display names of the required attendees, separated by semicolons.</summary>
    public string? RequiredAttendees => Properties.GetString(NamedProperties.ToAttendeesString.Name);

    /// <summary>Gets the display names of the optional attendees, separated by semicolons.</summary>
    public string? OptionalAttendees => Properties.GetString(NamedProperties.CcAttendeesString.Name);

    /// <summary>Gets whether a reminder is set.</summary>
    public bool ReminderSet => Properties.GetBoolean(NamedProperties.ReminderSet.Name) ?? false;

    /// <summary>Gets how long before the start the reminder fires.</summary>
    public TimeSpan? ReminderDelta => Properties.GetInt32(NamedProperties.ReminderDelta.Name) is int minutes ? TimeSpan.FromMinutes(minutes) : null;

    /// <summary>Gets whether the appointment is recurring.</summary>
    public bool IsRecurring => Properties.GetBoolean(NamedProperties.Recurring.Name) ?? Properties.Find(NamedProperties.AppointmentRecur.Name) is not null;

    /// <summary>Gets the description of the recurrence, such as "every Monday from 9:00 to 10:00".</summary>
    public string? RecurrenceDescription => Properties.GetString(NamedProperties.RecurrencePattern.Name);

    /// <summary>Gets the description of the time zone of the appointment.</summary>
    public string? TimeZoneDescription => Properties.GetString(NamedProperties.TimeZoneDescription.Name);

    /// <summary>Returns the recurrence of the appointment.</summary>
    /// <returns>The recurrence, or <see langword="null"/> when the appointment is not recurring.</returns>
    /// <exception cref="InvalidPstException">The recurrence is malformed.</exception>
    public PstRecurrencePattern? GetRecurrence()
    {
        var data = Properties.GetBinary(NamedProperties.AppointmentRecur.Name);
        return data is null ? null : PstRecurrencePattern.Parse(data);
    }
}
