namespace Meziantou.Framework.Email.Internals;

/// <summary>The named properties the library reads and writes ([MS-OXPROPS]), with their types.</summary>
internal static class NamedProperties
{
    // PSETID_Common
    public static readonly NamedPropertyDefinition ReminderDelta = Common(0x8501, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition ReminderTime = Common(0x8502, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition ReminderSet = Common(0x8503, MapiPropertyType.Boolean);
    public static readonly NamedPropertyDefinition Private = Common(0x8506, MapiPropertyType.Boolean);
    public static readonly NamedPropertyDefinition CommonStart = Common(0x8516, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition CommonEnd = Common(0x8517, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition ReminderSignalTime = Common(0x8560, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition Companies = Common(0x8539, MapiPropertyType.MultipleUnicode);
    public static readonly NamedPropertyDefinition Contacts = Common(0x853A, MapiPropertyType.MultipleUnicode);
    public static readonly NamedPropertyDefinition Keywords = new(new MapiNamedProperty(MapiPropertySets.PublicStrings, "Keywords"), MapiPropertyType.MultipleUnicode);

    // PSETID_Appointment
    public static readonly NamedPropertyDefinition BusyStatus = Appointment(0x8205, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition Location = Appointment(0x8208, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition AppointmentStartWhole = Appointment(0x820D, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition AppointmentEndWhole = Appointment(0x820E, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition AppointmentDuration = Appointment(0x8213, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition AppointmentSubType = Appointment(0x8215, MapiPropertyType.Boolean);
    public static readonly NamedPropertyDefinition AppointmentRecur = Appointment(0x8216, MapiPropertyType.Binary);
    public static readonly NamedPropertyDefinition AppointmentStateFlags = Appointment(0x8217, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition ResponseStatus = Appointment(0x8218, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition Recurring = Appointment(0x8223, MapiPropertyType.Boolean);
    public static readonly NamedPropertyDefinition RecurrenceType = Appointment(0x8231, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition RecurrencePattern = Appointment(0x8232, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition TimeZoneStruct = Appointment(0x8233, MapiPropertyType.Binary);
    public static readonly NamedPropertyDefinition TimeZoneDescription = Appointment(0x8234, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition ClipStart = Appointment(0x8235, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition ClipEnd = Appointment(0x8236, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition AllAttendeesString = Appointment(0x8238, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition ToAttendeesString = Appointment(0x823B, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition CcAttendeesString = Appointment(0x823C, MapiPropertyType.Unicode);

    // PSETID_Task
    public static readonly NamedPropertyDefinition TaskStatus = Task(0x8101, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition PercentComplete = Task(0x8102, MapiPropertyType.Floating64);
    public static readonly NamedPropertyDefinition TaskStartDate = Task(0x8104, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition TaskDueDate = Task(0x8105, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition TaskDateCompleted = Task(0x810F, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition TaskActualEffort = Task(0x8110, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition TaskEstimatedEffort = Task(0x8111, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition TaskRecurrence = Task(0x8116, MapiPropertyType.Binary);
    public static readonly NamedPropertyDefinition TaskComplete = Task(0x811C, MapiPropertyType.Boolean);
    public static readonly NamedPropertyDefinition TaskOwner = Task(0x811F, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition TaskAssigner = Task(0x8121, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition TaskFRecurring = Task(0x8126, MapiPropertyType.Boolean);

    // PSETID_Note ([MS-OXONOTE] 2.2.1)
    public static readonly NamedPropertyDefinition NoteColor = Note(0x8B00, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition NoteWidth = Note(0x8B02, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition NoteHeight = Note(0x8B03, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition NoteX = Note(0x8B04, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition NoteY = Note(0x8B05, MapiPropertyType.Integer32);

    // PSETID_Log ([MS-OXOJRNL] 2.2.1)
    public static readonly NamedPropertyDefinition LogType = Log(0x8700, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition LogStart = Log(0x8706, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition LogDuration = Log(0x8707, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition LogEnd = Log(0x8708, MapiPropertyType.Time);
    public static readonly NamedPropertyDefinition LogFlags = Log(0x870C, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition LogDocumentPrinted = Log(0x870E, MapiPropertyType.Boolean);
    public static readonly NamedPropertyDefinition LogDocumentSaved = Log(0x870F, MapiPropertyType.Boolean);
    public static readonly NamedPropertyDefinition LogDocumentRouted = Log(0x8710, MapiPropertyType.Boolean);
    public static readonly NamedPropertyDefinition LogDocumentPosted = Log(0x8711, MapiPropertyType.Boolean);
    public static readonly NamedPropertyDefinition LogTypeDescription = Log(0x8712, MapiPropertyType.Unicode);

    // PSETID_Address
    public static readonly NamedPropertyDefinition FileUnder = Address(0x8005, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition WorkAddress = Address(0x801B, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Html = Address(0x802B, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition WorkAddressStreet = Address(0x8045, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition WorkAddressCity = Address(0x8046, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition WorkAddressState = Address(0x8047, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition WorkAddressPostalCode = Address(0x8048, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition WorkAddressCountry = Address(0x8049, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition DistributionListChecksum = Address(0x804C, MapiPropertyType.Integer32);
    public static readonly NamedPropertyDefinition DistributionListName = Address(0x8053, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition DistributionListOneOffMembers = Address(0x8054, MapiPropertyType.MultipleBinary);
    public static readonly NamedPropertyDefinition DistributionListMembers = Address(0x8055, MapiPropertyType.MultipleBinary);
    public static readonly NamedPropertyDefinition InstantMessagingAddress = Address(0x8062, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email1DisplayName = Address(0x8080, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email1AddressType = Address(0x8082, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email1EmailAddress = Address(0x8083, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email1OriginalDisplayName = Address(0x8084, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email2DisplayName = Address(0x8090, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email2AddressType = Address(0x8092, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email2EmailAddress = Address(0x8093, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email2OriginalDisplayName = Address(0x8094, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email3DisplayName = Address(0x80A0, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email3AddressType = Address(0x80A2, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email3EmailAddress = Address(0x80A3, MapiPropertyType.Unicode);
    public static readonly NamedPropertyDefinition Email3OriginalDisplayName = Address(0x80A4, MapiPropertyType.Unicode);

    private static NamedPropertyDefinition Common(int id, MapiPropertyType type) => new(new MapiNamedProperty(MapiPropertySets.Common, id), type);

    private static NamedPropertyDefinition Appointment(int id, MapiPropertyType type) => new(new MapiNamedProperty(MapiPropertySets.Appointment, id), type);

    private static NamedPropertyDefinition Task(int id, MapiPropertyType type) => new(new MapiNamedProperty(MapiPropertySets.Task, id), type);

    private static NamedPropertyDefinition Address(int id, MapiPropertyType type) => new(new MapiNamedProperty(MapiPropertySets.Address, id), type);

    private static NamedPropertyDefinition Note(int id, MapiPropertyType type) => new(new MapiNamedProperty(MapiPropertySets.Note, id), type);

    private static NamedPropertyDefinition Log(int id, MapiPropertyType type) => new(new MapiNamedProperty(MapiPropertySets.Log, id), type);
}
