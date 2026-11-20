using Meziantou.Framework.Mail.Internals;
using Meziantou.Framework.Mail.Internals.Ltp;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail;

/// <summary>A task (IPM.Task).</summary>
public sealed class PstTask : PstMessage
{
    internal PstTask(PstContext context, NdbNode node, PstPropertyCollection properties, int embeddingDepth)
        : base(context, node, properties, embeddingDepth)
    {
    }

    /// <summary>Gets the status of the task.</summary>
    public PstTaskStatus Status => (PstTaskStatus)(Properties.GetInt32(NamedProperties.TaskStatus.Name) ?? (int)PstTaskStatus.NotStarted);

    /// <summary>Gets the completion of the task, from 0 to 1.</summary>
    public double PercentComplete => Properties.TryGetValue<double>(NamedProperties.PercentComplete.Name, out var value) ? value : 0;

    /// <summary>Gets whether the task is complete.</summary>
    public bool IsComplete => Properties.GetBoolean(NamedProperties.TaskComplete.Name) ?? Status == PstTaskStatus.Complete;

    /// <summary>Gets the date the task starts, at midnight UTC of the local date.</summary>
    public DateTime? StartDate => Properties.GetDateTime(NamedProperties.TaskStartDate.Name);

    /// <summary>Gets the date the task is due, at midnight UTC of the local date.</summary>
    public DateTime? DueDate => Properties.GetDateTime(NamedProperties.TaskDueDate.Name);

    /// <summary>Gets the date the task was completed.</summary>
    public DateTime? DateCompleted => Properties.GetDateTime(NamedProperties.TaskDateCompleted.Name);

    /// <summary>Gets the name of the owner of the task.</summary>
    public string? Owner => Properties.GetString(NamedProperties.TaskOwner.Name);

    /// <summary>Gets the name of the user who assigned the task.</summary>
    public string? Assigner => Properties.GetString(NamedProperties.TaskAssigner.Name);

    /// <summary>Gets the time spent on the task.</summary>
    public TimeSpan? ActualEffort => Properties.GetInt32(NamedProperties.TaskActualEffort.Name) is int minutes ? TimeSpan.FromMinutes(minutes) : null;

    /// <summary>Gets the time the task is estimated to take.</summary>
    public TimeSpan? EstimatedEffort => Properties.GetInt32(NamedProperties.TaskEstimatedEffort.Name) is int minutes ? TimeSpan.FromMinutes(minutes) : null;

    /// <summary>Gets whether the task is recurring.</summary>
    public bool IsRecurring => Properties.GetBoolean(NamedProperties.TaskFRecurring.Name) ?? false;

    /// <summary>Gets whether a reminder is set.</summary>
    public bool ReminderSet => Properties.GetBoolean(NamedProperties.ReminderSet.Name) ?? false;

    /// <summary>Gets the date and time, in UTC, of the reminder.</summary>
    public DateTime? ReminderTime => Properties.GetDateTime(NamedProperties.ReminderTime.Name);

    /// <summary>Returns the recurrence of the task.</summary>
    /// <returns>The recurrence, or <see langword="null"/> when the task is not recurring.</returns>
    /// <exception cref="InvalidPstException">The recurrence is malformed.</exception>
    public PstRecurrencePattern? GetRecurrence()
    {
        var data = Properties.GetBinary(NamedProperties.TaskRecurrence.Name);
        return data is null ? null : PstRecurrencePattern.Parse(data);
    }
}
