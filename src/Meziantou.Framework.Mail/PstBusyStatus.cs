namespace Meziantou.Framework.Mail;

/// <summary>How an appointment shows the time of the user (PidLidBusyStatus).</summary>
public enum PstBusyStatus
{
    /// <summary>Free.</summary>
    Free = 0,

    /// <summary>Tentative.</summary>
    Tentative = 1,

    /// <summary>Busy.</summary>
    Busy = 2,

    /// <summary>Out of office.</summary>
    OutOfOffice = 3,

    /// <summary>Working elsewhere.</summary>
    WorkingElsewhere = 4,
}
