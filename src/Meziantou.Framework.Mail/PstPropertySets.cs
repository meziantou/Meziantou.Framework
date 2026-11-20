namespace Meziantou.Framework.Mail;

/// <summary>The GUIDs of the common property sets of named properties ([MS-OXPROPS] 1.3.2).</summary>
public static class PstPropertySets
{
    /// <summary>Gets the PS_MAPI property set: named properties that map to MAPI property identifiers.</summary>
    public static Guid Mapi { get; } = new("00020328-0000-0000-c000-000000000046");

    /// <summary>Gets the PS_PUBLIC_STRINGS property set: properties named by applications, such as keywords (categories).</summary>
    public static Guid PublicStrings { get; } = new("00020329-0000-0000-c000-000000000046");

    /// <summary>Gets the PS_INTERNET_HEADERS property set: Internet headers, named by the header name.</summary>
    public static Guid InternetHeaders { get; } = new("00020386-0000-0000-c000-000000000046");

    /// <summary>Gets the PSETID_Common property set: properties common to all items, such as reminders.</summary>
    public static Guid Common { get; } = new("00062008-0000-0000-c000-000000000046");

    /// <summary>Gets the PSETID_Address property set: the properties of contacts and distribution lists.</summary>
    public static Guid Address { get; } = new("00062004-0000-0000-c000-000000000046");

    /// <summary>Gets the PSETID_Appointment property set: the properties of appointments and meetings.</summary>
    public static Guid Appointment { get; } = new("00062002-0000-0000-c000-000000000046");

    /// <summary>Gets the PSETID_Meeting property set: the properties of meeting requests and responses.</summary>
    public static Guid Meeting { get; } = new("6ed8da90-450b-101b-98da-00aa003f1305");

    /// <summary>Gets the PSETID_Task property set: the properties of tasks.</summary>
    public static Guid Task { get; } = new("00062003-0000-0000-c000-000000000046");

    /// <summary>Gets the PSETID_Note property set: the properties of notes.</summary>
    public static Guid Note { get; } = new("0006200e-0000-0000-c000-000000000046");

    /// <summary>Gets the PSETID_Log property set: the properties of journal entries.</summary>
    public static Guid Log { get; } = new("0006200a-0000-0000-c000-000000000046");
}
