namespace Meziantou.Framework.Mail;

/// <summary>An occurrence of a recurring appointment that differs from the pattern ([MS-OXOCAL] 2.2.1.44.2, ExceptionInfo and ExtendedException). The properties that are <see langword="null"/> keep the value of the appointment.</summary>
public sealed class PstRecurrenceException
{
    /// <summary>Gets or sets the date and time the occurrence starts, in the time zone of the appointment.</summary>
    public DateTime StartDateTime { get; set; }

    /// <summary>Gets or sets the date and time the occurrence ends, in the time zone of the appointment.</summary>
    public DateTime EndDateTime { get; set; }

    /// <summary>Gets or sets the date and time the occurrence would have started without the exception, in the time zone of the appointment.</summary>
    public DateTime OriginalStartDate { get; set; }

    /// <summary>Gets or sets the subject of the occurrence.</summary>
    public string? Subject { get; set; }

    /// <summary>Gets or sets the location of the occurrence.</summary>
    public string? Location { get; set; }

    /// <summary>Gets or sets the meeting type of the occurrence (PidLidAppointmentStateFlags).</summary>
    public int? MeetingType { get; set; }

    /// <summary>Gets or sets the number of minutes before the occurrence when the reminder fires.</summary>
    public int? ReminderDelta { get; set; }

    /// <summary>Gets or sets whether the occurrence has a reminder.</summary>
    public bool? ReminderSet { get; set; }

    /// <summary>Gets or sets the busy status of the occurrence.</summary>
    public PstBusyStatus? BusyStatus { get; set; }

    /// <summary>Gets or sets whether the occurrence has attachments.</summary>
    public bool? HasAttachment { get; set; }

    /// <summary>Gets or sets whether the occurrence is an all-day event.</summary>
    public bool? IsAllDay { get; set; }

    /// <summary>Gets or sets the color of the occurrence (deprecated by Outlook).</summary>
    public int? AppointmentColor { get; set; }

    /// <summary>Gets or sets whether the occurrence has its own body, stored in the attachment of the exception.</summary>
    public bool HasExceptionalBody { get; set; }
}
