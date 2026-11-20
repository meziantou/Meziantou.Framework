namespace Meziantou.Framework.Mail;

/// <summary>The status of a task (PidLidTaskStatus).</summary>
public enum PstTaskStatus
{
    /// <summary>The task has not started.</summary>
    NotStarted = 0,

    /// <summary>The task is in progress.</summary>
    InProgress = 1,

    /// <summary>The task is complete.</summary>
    Complete = 2,

    /// <summary>The task is waiting on someone else.</summary>
    Waiting = 3,

    /// <summary>The task is deferred.</summary>
    Deferred = 4,
}
