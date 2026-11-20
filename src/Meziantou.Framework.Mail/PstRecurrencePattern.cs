using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>The recurrence of an appointment ([MS-OXOCAL] 2.2.1.44, the PidLidAppointmentRecur property). The dates are in the time zone of the appointment.</summary>
public sealed class PstRecurrencePattern
{
    private const ushort ReaderVersion = 0x3004;
    private const ushort WriterVersion = 0x3004;
    private const uint ReaderVersion2 = 0x3006;
    private const uint WriterVersion2 = 0x3009;
    private const uint WriterVersion2WithChangeHighlight = 0x3009;
    private static readonly DateTime Origin = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Gets or sets how often the recurrence repeats.</summary>
    public PstRecurrenceFrequency Frequency { get; set; } = PstRecurrenceFrequency.Weekly;

    /// <summary>Gets or sets the type of the pattern.</summary>
    public PstRecurrencePatternType PatternType { get; set; } = PstRecurrencePatternType.Week;

    /// <summary>Gets or sets the calendar of the pattern (0 for the Gregorian calendar, [MS-OXOCAL] 2.2.1.44.1).</summary>
    public int CalendarType { get; set; }

    /// <summary>Gets or sets the first occurrence of the pattern, as a number of minutes ([MS-OXOCAL] 2.2.1.44.1.2 describes how Outlook computes it).</summary>
    public int FirstDateTime { get; set; }

    /// <summary>Gets or sets the interval: a number of minutes for a daily pattern, of weeks for a weekly pattern, of months for a monthly or yearly pattern.</summary>
    public int Period { get; set; } = 1;

    /// <summary>Gets or sets the sliding flag of the pattern, used by tasks that regenerate.</summary>
    public int SlidingFlag { get; set; }

    /// <summary>Gets or sets the days of the week of a <see cref="PstRecurrencePatternType.Week"/> or <see cref="PstRecurrencePatternType.MonthNth"/> pattern.</summary>
    public PstDaysOfWeek DaysOfWeek { get; set; }

    /// <summary>Gets or sets the day of the month of a <see cref="PstRecurrencePatternType.Month"/> pattern.</summary>
    public int DayOfMonth { get; set; }

    /// <summary>Gets or sets the week of the month (1 to 4, 5 for the last one) of a <see cref="PstRecurrencePatternType.MonthNth"/> pattern.</summary>
    public int WeekOfMonth { get; set; }

    /// <summary>Gets or sets how the recurrence ends.</summary>
    public PstRecurrenceEndType EndType { get; set; } = PstRecurrenceEndType.NeverEnd;

    /// <summary>Gets or sets the number of occurrences, when the recurrence ends after a number of occurrences.</summary>
    public int OccurrenceCount { get; set; } = 10;

    /// <summary>Gets or sets the first day of the week.</summary>
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Sunday;

    /// <summary>Gets the dates of the occurrences that are deleted or modified (at midnight, in the original dates of the occurrences).</summary>
    public IList<DateTime> DeletedInstanceDates { get; } = [];

    /// <summary>Gets the dates of the occurrences that are modified (at midnight, in the new start dates of the occurrences).</summary>
    public IList<DateTime> ModifiedInstanceDates { get; } = [];

    /// <summary>Gets or sets the date of the first occurrence (at midnight).</summary>
    public DateTime StartDate { get; set; } = Origin;

    /// <summary>Gets or sets the date of the last occurrence (at midnight); December 31, 4500 when the recurrence never ends.</summary>
    public DateTime EndDate { get; set; } = new(4500, 12, 31, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Gets or sets the start time of the occurrences, as an offset from midnight.</summary>
    public TimeSpan StartTimeOffset { get; set; }

    /// <summary>Gets or sets the end time of the occurrences, as an offset from the midnight of their start.</summary>
    public TimeSpan EndTimeOffset { get; set; }

    /// <summary>Gets the occurrences that differ from the pattern.</summary>
    public IList<PstRecurrenceException> Exceptions { get; } = [];

    /// <summary>Gets or sets the version of the writer of the blob ([MS-OXOCAL] 2.2.1.44.3, WriterVersion2).</summary>
    internal uint WriterVersion2Value { get; set; } = WriterVersion2;

    /// <summary>Parses an AppointmentRecurrencePattern, or only a RecurrencePattern when the data stops after it (such as the recurrence of a task).</summary>
    /// <exception cref="InvalidPstException">The data is not a valid recurrence pattern.</exception>
    internal static PstRecurrencePattern Parse(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data);
        var pattern = new PstRecurrencePattern();
        var readerVersion = reader.ReadUInt16();
        var writerVersion = reader.ReadUInt16();
        if (readerVersion != ReaderVersion || writerVersion != WriterVersion)
        {
            throw new InvalidPstException($"The recurrence pattern has the versions 0x{readerVersion:X4} and 0x{writerVersion:X4} instead of 0x3004.");
        }

        pattern.Frequency = (PstRecurrenceFrequency)reader.ReadUInt16();
        pattern.PatternType = (PstRecurrencePatternType)reader.ReadUInt16();
        pattern.CalendarType = reader.ReadUInt16();
        pattern.FirstDateTime = reader.ReadInt32();
        pattern.Period = reader.ReadInt32();
        pattern.SlidingFlag = reader.ReadInt32();
        switch (pattern.PatternType)
        {
            case PstRecurrencePatternType.Day:
                break;
            case PstRecurrencePatternType.Week:
                pattern.DaysOfWeek = (PstDaysOfWeek)reader.ReadInt32();
                break;
            case PstRecurrencePatternType.Month or PstRecurrencePatternType.MonthEnd or PstRecurrencePatternType.HijriMonth or PstRecurrencePatternType.HijriMonthEnd:
                pattern.DayOfMonth = reader.ReadInt32();
                break;
            case PstRecurrencePatternType.MonthNth or PstRecurrencePatternType.HijriMonthNth:
                pattern.DaysOfWeek = (PstDaysOfWeek)reader.ReadInt32();
                pattern.WeekOfMonth = reader.ReadInt32();
                break;
            default:
                throw new InvalidPstException($"The recurrence pattern has an unknown type 0x{(int)pattern.PatternType:X4}.");
        }

        var endType = reader.ReadUInt32();
        pattern.EndType = endType == 0xFFFFFFFF ? PstRecurrenceEndType.NeverEnd : (PstRecurrenceEndType)endType;
        pattern.OccurrenceCount = reader.ReadInt32();
        pattern.FirstDayOfWeek = (DayOfWeek)(reader.ReadInt32() % 7);
        var deletedCount = reader.ReadCount(4);
        for (var i = 0; i < deletedCount; i++)
        {
            pattern.DeletedInstanceDates.Add(reader.ReadDate());
        }

        var modifiedCount = reader.ReadCount(4);
        for (var i = 0; i < modifiedCount; i++)
        {
            pattern.ModifiedInstanceDates.Add(reader.ReadDate());
        }

        pattern.StartDate = reader.ReadDate();
        pattern.EndDate = reader.ReadDate();
        if (reader.Remaining == 0)
        {
            return pattern;
        }

        // AppointmentRecurrencePattern
        var readerVersion2 = reader.ReadUInt32();
        pattern.WriterVersion2Value = reader.ReadUInt32();
        if (readerVersion2 != ReaderVersion2)
        {
            throw new InvalidPstException($"The appointment recurrence pattern has the version 0x{readerVersion2:X8} instead of 0x3006.");
        }

        pattern.StartTimeOffset = TimeSpan.FromMinutes(reader.ReadUInt32());
        pattern.EndTimeOffset = TimeSpan.FromMinutes(reader.ReadUInt32());
        var exceptionCount = reader.ReadUInt16();
        var flags = new List<ushort>(exceptionCount);
        for (var i = 0; i < exceptionCount; i++)
        {
            var exception = new PstRecurrenceException
            {
                StartDateTime = reader.ReadDate(),
                EndDateTime = reader.ReadDate(),
                OriginalStartDate = reader.ReadDate(),
            };

            var overrideFlags = reader.ReadUInt16();
            flags.Add(overrideFlags);
            if ((overrideFlags & OverrideFlags.Subject) != 0)
            {
                reader.ReadUInt16(); // SubjectLength: the length plus one
                var length = reader.ReadUInt16();
                exception.Subject = Encoding.Latin1.GetString(reader.ReadBytes(length));
            }

            if ((overrideFlags & OverrideFlags.MeetingType) != 0)
            {
                exception.MeetingType = reader.ReadInt32();
            }

            if ((overrideFlags & OverrideFlags.ReminderDelta) != 0)
            {
                exception.ReminderDelta = reader.ReadInt32();
            }

            if ((overrideFlags & OverrideFlags.Reminder) != 0)
            {
                exception.ReminderSet = reader.ReadInt32() != 0;
            }

            if ((overrideFlags & OverrideFlags.Location) != 0)
            {
                reader.ReadUInt16();
                var length = reader.ReadUInt16();
                exception.Location = Encoding.Latin1.GetString(reader.ReadBytes(length));
            }

            if ((overrideFlags & OverrideFlags.BusyStatus) != 0)
            {
                exception.BusyStatus = (PstBusyStatus)reader.ReadInt32();
            }

            if ((overrideFlags & OverrideFlags.Attachment) != 0)
            {
                exception.HasAttachment = reader.ReadInt32() != 0;
            }

            if ((overrideFlags & OverrideFlags.SubType) != 0)
            {
                exception.IsAllDay = reader.ReadInt32() != 0;
            }

            if ((overrideFlags & OverrideFlags.AppointmentColor) != 0)
            {
                exception.AppointmentColor = reader.ReadInt32();
            }

            exception.HasExceptionalBody = (overrideFlags & OverrideFlags.ExceptionalBody) != 0;
            pattern.Exceptions.Add(exception);
        }

        reader.Skip(reader.ReadCount(1)); // ReservedBlock1
        if (reader.Remaining == 0)
        {
            return pattern;
        }

        // ExtendedException: the Unicode subject and location of the exceptions
        for (var i = 0; i < exceptionCount; i++)
        {
            var exception = pattern.Exceptions[i];
            if (pattern.WriterVersion2Value >= WriterVersion2WithChangeHighlight)
            {
                reader.Skip(reader.ReadCount(1)); // ChangeHighlight
            }

            reader.Skip(reader.ReadCount(1)); // ReservedBlockEE1
            if ((flags[i] & (OverrideFlags.Subject | OverrideFlags.Location)) != 0)
            {
                reader.ReadDate();
                reader.ReadDate();
                reader.ReadDate();
                if ((flags[i] & OverrideFlags.Subject) != 0)
                {
                    exception.Subject = reader.ReadWideString();
                }

                if ((flags[i] & OverrideFlags.Location) != 0)
                {
                    exception.Location = reader.ReadWideString();
                }

                reader.Skip(reader.ReadCount(1)); // ReservedBlockEE2
            }
        }

        if (reader.Remaining >= 4)
        {
            reader.Skip(reader.ReadCount(1)); // ReservedBlock2
        }

        return pattern;
    }

    /// <summary>Writes the pattern as an AppointmentRecurrencePattern.</summary>
    internal byte[] ToByteArray(bool appointment = true)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(ReaderVersion);
        writer.Write(WriterVersion);
        writer.Write((ushort)Frequency);
        writer.Write((ushort)PatternType);
        writer.Write((ushort)CalendarType);
        writer.Write(FirstDateTime);
        writer.Write(Period);
        writer.Write(SlidingFlag);
        switch (PatternType)
        {
            case PstRecurrencePatternType.Week:
                writer.Write((int)DaysOfWeek);
                break;
            case PstRecurrencePatternType.Month or PstRecurrencePatternType.MonthEnd or PstRecurrencePatternType.HijriMonth or PstRecurrencePatternType.HijriMonthEnd:
                writer.Write(DayOfMonth);
                break;
            case PstRecurrencePatternType.MonthNth or PstRecurrencePatternType.HijriMonthNth:
                writer.Write((int)DaysOfWeek);
                writer.Write(WeekOfMonth);
                break;
        }

        writer.Write((uint)EndType);
        writer.Write(OccurrenceCount);
        writer.Write((int)FirstDayOfWeek);
        writer.Write(DeletedInstanceDates.Count);
        foreach (var date in DeletedInstanceDates)
        {
            writer.Write(ToMinutes(date));
        }

        writer.Write(ModifiedInstanceDates.Count);
        foreach (var date in ModifiedInstanceDates)
        {
            writer.Write(ToMinutes(date));
        }

        writer.Write(ToMinutes(StartDate));
        writer.Write(ToMinutes(EndDate));
        if (!appointment)
        {
            writer.Flush();
            return stream.ToArray();
        }

        writer.Write(ReaderVersion2);
        writer.Write(WriterVersion2);
        writer.Write((uint)StartTimeOffset.TotalMinutes);
        writer.Write((uint)EndTimeOffset.TotalMinutes);
        writer.Write((ushort)Exceptions.Count);
        var allFlags = new List<ushort>(Exceptions.Count);
        foreach (var exception in Exceptions)
        {
            writer.Write(ToMinutes(exception.StartDateTime));
            writer.Write(ToMinutes(exception.EndDateTime));
            writer.Write(ToMinutes(exception.OriginalStartDate));
            var flags = GetFlags(exception);
            allFlags.Add(flags);
            writer.Write(flags);
            if (exception.Subject is not null)
            {
                WriteAnsi(writer, exception.Subject);
            }

            if (exception.MeetingType is int meetingType)
            {
                writer.Write(meetingType);
            }

            if (exception.ReminderDelta is int reminderDelta)
            {
                writer.Write(reminderDelta);
            }

            if (exception.ReminderSet is bool reminderSet)
            {
                writer.Write(reminderSet ? 1 : 0);
            }

            if (exception.Location is not null)
            {
                WriteAnsi(writer, exception.Location);
            }

            if (exception.BusyStatus is PstBusyStatus busyStatus)
            {
                writer.Write((int)busyStatus);
            }

            if (exception.HasAttachment is bool hasAttachment)
            {
                writer.Write(hasAttachment ? 1 : 0);
            }

            if (exception.IsAllDay is bool isAllDay)
            {
                writer.Write(isAllDay ? 1 : 0);
            }

            if (exception.AppointmentColor is int color)
            {
                writer.Write(color);
            }
        }

        writer.Write(0); // ReservedBlock1Size
        for (var i = 0; i < Exceptions.Count; i++)
        {
            var exception = Exceptions[i];
            writer.Write(4); // ChangeHighlightSize
            writer.Write(0); // ChangeHighlightValue
            writer.Write(0); // ReservedBlockEE1Size
            if ((allFlags[i] & (OverrideFlags.Subject | OverrideFlags.Location)) != 0)
            {
                writer.Write(ToMinutes(exception.StartDateTime));
                writer.Write(ToMinutes(exception.EndDateTime));
                writer.Write(ToMinutes(exception.OriginalStartDate));
                if (exception.Subject is not null)
                {
                    WriteWide(writer, exception.Subject);
                }

                if (exception.Location is not null)
                {
                    WriteWide(writer, exception.Location);
                }

                writer.Write(0); // ReservedBlockEE2Size
            }
        }

        writer.Write(0); // ReservedBlock2Size
        writer.Flush();
        return stream.ToArray();
    }

    internal static DateTime FromMinutes(uint minutes) => Origin.AddMinutes(minutes);

    internal static uint ToMinutes(DateTime value) => value <= Origin ? 0 : (uint)Math.Min(uint.MaxValue, (long)(value - Origin).TotalMinutes);

    private static ushort GetFlags(PstRecurrenceException exception)
    {
        ushort flags = 0;
        if (exception.Subject is not null)
        {
            flags |= OverrideFlags.Subject;
        }

        if (exception.MeetingType is not null)
        {
            flags |= OverrideFlags.MeetingType;
        }

        if (exception.ReminderDelta is not null)
        {
            flags |= OverrideFlags.ReminderDelta;
        }

        if (exception.ReminderSet is not null)
        {
            flags |= OverrideFlags.Reminder;
        }

        if (exception.Location is not null)
        {
            flags |= OverrideFlags.Location;
        }

        if (exception.BusyStatus is not null)
        {
            flags |= OverrideFlags.BusyStatus;
        }

        if (exception.HasAttachment is not null)
        {
            flags |= OverrideFlags.Attachment;
        }

        if (exception.IsAllDay is not null)
        {
            flags |= OverrideFlags.SubType;
        }

        if (exception.AppointmentColor is not null)
        {
            flags |= OverrideFlags.AppointmentColor;
        }

        if (exception.HasExceptionalBody)
        {
            flags |= OverrideFlags.ExceptionalBody;
        }

        return flags;
    }

    private static void WriteAnsi(BinaryWriter writer, string value)
    {
        var bytes = PstContext.GetEncoding(1252).GetBytes(value);
        var length = (ushort)Math.Min(bytes.Length, ushort.MaxValue - 1);
        writer.Write((ushort)(length + 1));
        writer.Write(length);
        writer.Write(bytes, 0, length);
    }

    private static void WriteWide(BinaryWriter writer, string value)
    {
        var length = (ushort)Math.Min(value.Length, ushort.MaxValue);
        writer.Write(length);
        foreach (var c in value.AsSpan(0, length))
        {
            writer.Write((ushort)c);
        }
    }

    private static class OverrideFlags
    {
        public const ushort Subject = 0x0001;
        public const ushort MeetingType = 0x0002;
        public const ushort ReminderDelta = 0x0004;
        public const ushort Reminder = 0x0008;
        public const ushort Location = 0x0010;
        public const ushort BusyStatus = 0x0020;
        public const ushort Attachment = 0x0040;
        public const ushort SubType = 0x0080;
        public const ushort AppointmentColor = 0x0100;
        public const ushort ExceptionalBody = 0x0200;
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public Reader(ReadOnlySpan<byte> data)
        {
            _data = data;
        }

        public readonly int Remaining => _data.Length - _position;

        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            if (length < 0 || length > Remaining)
            {
                throw new InvalidPstException("The recurrence pattern is truncated.");
            }

            var result = _data.Slice(_position, length);
            _position += length;
            return result;
        }

        public void Skip(int length) => ReadBytes(length);

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));

        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4));

        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(4));

        public DateTime ReadDate() => FromMinutes(ReadUInt32());

        /// <summary>Reads a 32-bit count of elements of <paramref name="elementSize"/> bytes, checked against the remaining data.</summary>
        public int ReadCount(int elementSize)
        {
            var count = ReadUInt32();
            if (count > (uint)(Remaining / elementSize))
            {
                throw new InvalidPstException("The recurrence pattern is truncated.");
            }

            return (int)count;
        }

        public string ReadWideString()
        {
            var length = ReadUInt16();
            var bytes = ReadBytes(length * 2);
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
            }

            return new string(chars);
        }
    }
}
