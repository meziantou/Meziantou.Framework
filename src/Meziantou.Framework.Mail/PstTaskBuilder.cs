using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>A task (IPM.Task) to add to a folder of a <see cref="PstWriter"/>.</summary>
public sealed class PstTaskBuilder : PstMessageBuilder
{
    /// <summary>Initializes a new instance of the <see cref="PstTaskBuilder"/> class.</summary>
    public PstTaskBuilder()
        : base("IPM.Task")
    {
    }

    /// <summary>Gets or sets the status of the task.</summary>
    public PstTaskStatus Status
    {
        get => Properties.Get(NamedProperties.TaskStatus.Name) is int value ? (PstTaskStatus)value : PstTaskStatus.NotStarted;
        set
        {
            Properties.SetValue(NamedProperties.TaskStatus, (int)value);
            Properties.SetValue(NamedProperties.TaskComplete, value == PstTaskStatus.Complete);
        }
    }

    /// <summary>Gets or sets the completion of the task, from 0 to 1.</summary>
    public double PercentComplete
    {
        get => Properties.Get(NamedProperties.PercentComplete.Name) is double value ? value : 0;
        set => Properties.SetValue(NamedProperties.PercentComplete, value);
    }

    /// <summary>Gets or sets the date the task starts.</summary>
    public DateTime? StartDate
    {
        get => Properties.Get(NamedProperties.TaskStartDate.Name) as DateTime?;
        set
        {
            Properties.SetValue(NamedProperties.TaskStartDate, value);
            Properties.SetValue(NamedProperties.CommonStart, value);
        }
    }

    /// <summary>Gets or sets the date the task is due.</summary>
    public DateTime? DueDate
    {
        get => Properties.Get(NamedProperties.TaskDueDate.Name) as DateTime?;
        set
        {
            Properties.SetValue(NamedProperties.TaskDueDate, value);
            Properties.SetValue(NamedProperties.CommonEnd, value);
        }
    }

    /// <summary>Gets or sets the date the task was completed.</summary>
    public DateTime? DateCompleted
    {
        get => Properties.Get(NamedProperties.TaskDateCompleted.Name) as DateTime?;
        set => Properties.SetValue(NamedProperties.TaskDateCompleted, value);
    }

    /// <summary>Gets or sets the name of the owner of the task.</summary>
    public string? Owner
    {
        get => Properties.GetValue<string>(NamedProperties.TaskOwner);
        set => Properties.SetValue(NamedProperties.TaskOwner, value);
    }

    /// <summary>Gets or sets the time spent on the task.</summary>
    public TimeSpan? ActualEffort
    {
        get => Properties.Get(NamedProperties.TaskActualEffort.Name) is int minutes ? TimeSpan.FromMinutes(minutes) : null;
        set => Properties.SetValue(NamedProperties.TaskActualEffort, value is null ? null : (int)value.Value.TotalMinutes);
    }

    /// <summary>Gets or sets the time the task is estimated to take.</summary>
    public TimeSpan? EstimatedEffort
    {
        get => Properties.Get(NamedProperties.TaskEstimatedEffort.Name) is int minutes ? TimeSpan.FromMinutes(minutes) : null;
        set => Properties.SetValue(NamedProperties.TaskEstimatedEffort, value is null ? null : (int)value.Value.TotalMinutes);
    }
}
