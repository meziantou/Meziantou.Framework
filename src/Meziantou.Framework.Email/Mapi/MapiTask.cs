using Meziantou.Framework.Email.Internals;
using Meziantou.Framework.Email.Internals.Ltp;
using Meziantou.Framework.Email.Internals.Ndb;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>A task (IPM.Task).</summary>
public sealed class MapiTask : MapiMessage
{
    internal MapiTask(MessageSource source, MapiPropertyCollection properties, int embeddingDepth)
        : base(source, properties, embeddingDepth)
    {
    }

    /// <summary>Gets the status of the task.</summary>
    public MapiTaskStatus Status => (MapiTaskStatus)(Properties.GetInt32(NamedProperties.TaskStatus.Name) ?? (int)MapiTaskStatus.NotStarted);

    /// <summary>Gets the completion of the task, from 0 to 1.</summary>
    public double PercentComplete => Properties.TryGetValue<double>(NamedProperties.PercentComplete.Name, out var value) ? value : 0;

    /// <summary>Gets whether the task is complete.</summary>
    public bool IsComplete => Properties.GetBoolean(NamedProperties.TaskComplete.Name) ?? Status == MapiTaskStatus.Complete;

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
    /// <exception cref="InvalidEmailDataException">The recurrence is malformed.</exception>
    public MapiRecurrencePattern? GetRecurrence()
    {
        var data = Properties.GetBinary(NamedProperties.TaskRecurrence.Name);
        return data is null ? null : MapiRecurrencePattern.Parse(data);
    }
}
