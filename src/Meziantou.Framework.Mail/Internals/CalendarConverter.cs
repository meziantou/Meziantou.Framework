using System.Security.Cryptography;
using System.Text;

namespace Meziantou.Framework.Mail.Internals;

/// <summary>Converts the appointments, tasks and contacts of Outlook to and from iCalendar and vCard.</summary>
internal static class CalendarConverter
{
    private static readonly DateTime Origin = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static VCard ToVCard(PstContact contact)
    {
        var card = new VCard
        {
            FormattedName = contact.DisplayName,
            FamilyName = contact.Surname,
            GivenName = contact.GivenName,
            AdditionalNames = contact.MiddleName,
            NamePrefix = contact.DisplayNamePrefix,
            NameSuffix = contact.Generation,
            Organization = contact.CompanyName,
            Title = contact.JobTitle,
            Birthday = contact.Birthday is { } birthday ? DateOnly.FromDateTime(birthday.AddHours(12)) : null,
            Note = contact.Body,
        };

        if (contact.Nickname is { } nickname)
        {
            card.Nicknames.Add(nickname);
        }

        if (contact.DepartmentName is { } department)
        {
            card.OrganizationUnits.Add(department);
        }

        var first = true;
        foreach (var email in contact.EmailAddresses)
        {
            card.Emails.Add(first ? new VCardValue(email, "internet", "pref") : new VCardValue(email, "internet"));
            first = false;
        }

        AddPhone(card, contact.BusinessTelephoneNumber, "work", "voice");
        AddPhone(card, contact.HomeTelephoneNumber, "home", "voice");
        AddPhone(card, contact.MobileTelephoneNumber, "cell", "voice");
        AddPhone(card, contact.BusinessFaxNumber, "work", "fax");
        AddAddress(card, contact.BusinessAddress, "work");
        AddAddress(card, contact.HomeAddress, "home");
        if (contact.BusinessHomePage is { } work)
        {
            card.Urls.Add(new VCardValue(work, "work"));
        }

        if (contact.PersonalHomePage is { } personal)
        {
            card.Urls.Add(new VCardValue(personal, "home"));
        }

        foreach (var category in contact.Categories)
        {
            card.Categories.Add(category);
        }

        return card;
    }

    public static PstContactBuilder ToContact(VCard card)
    {
        var builder = new PstContactBuilder
        {
            DisplayName = card.FormattedName,
            Surname = card.FamilyName,
            GivenName = card.GivenName,
            MiddleName = card.AdditionalNames,
            Nickname = card.Nicknames.FirstOrDefault(),
            CompanyName = card.Organization,
            DepartmentName = card.OrganizationUnits.FirstOrDefault(),
            JobTitle = card.Title,
            Birthday = card.Birthday?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Body = card.Note,
        };

        builder.Properties.SetValue(PropertyIds.DisplayNamePrefix, PstPropertyType.Unicode, card.NamePrefix);
        builder.Properties.SetValue(PropertyIds.Generation, PstPropertyType.Unicode, card.NameSuffix);
        var emails = card.Emails.OrderByDescending(email => email.HasType("pref")).Select(email => email.Value).ToList();
        builder.Email1Address = emails.ElementAtOrDefault(0);
        builder.Email2Address = emails.ElementAtOrDefault(1);
        builder.Email3Address = emails.ElementAtOrDefault(2);
        foreach (var phone in card.Phones)
        {
            if (phone.HasType("fax"))
            {
                builder.Properties.SetValue(PropertyIds.BusinessFaxNumber, PstPropertyType.Unicode, phone.Value);
            }
            else if (phone.HasType("cell"))
            {
                builder.MobileTelephoneNumber ??= phone.Value;
            }
            else if (phone.HasType("home"))
            {
                builder.HomeTelephoneNumber ??= phone.Value;
            }
            else
            {
                builder.BusinessTelephoneNumber ??= phone.Value;
            }
        }

        foreach (var address in card.Addresses)
        {
            var postal = new PstPostalAddress
            {
                Street = string.Join("\n", new[] { address.PostOfficeBox, address.ExtendedAddress, address.Street }.Where(part => !string.IsNullOrEmpty(part))) is { Length: > 0 } street ? street : null,
                City = address.Locality,
                StateOrProvince = address.Region,
                PostalCode = address.PostalCode,
                Country = address.Country,
            };

            if (address.HasType("home"))
            {
                builder.SetHomeAddress(postal);
            }
            else
            {
                builder.SetBusinessAddress(postal);
            }
        }

        foreach (var url in card.Urls)
        {
            if (url.HasType("home"))
            {
                builder.Properties.SetValue(PropertyIds.PersonalHomePage, PstPropertyType.Unicode, url.Value);
            }
            else
            {
                builder.BusinessHomePage ??= url.Value;
            }
        }

        if (card.Categories.Count > 0)
        {
            builder.Categories = [.. card.Categories];
        }

        return builder;
    }

    public static VEvent AddAppointment(VCalendar calendar, PstAppointment appointment)
    {
        var uid = CreateUid(appointment);
        var master = calendar.Add(new VEvent());
        master.Uid = uid;
        master.Stamp = CalendarDateTime.FromUtc(appointment.LastModificationTime ?? appointment.CreationTime ?? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        master.Summary = appointment.Subject;
        master.Description = appointment.Body;
        master.Location = appointment.Location;
        SetTimes(master, appointment.Start, appointment.End, appointment.IsAllDay);
        master.Transparency = appointment.BusyStatus == PstBusyStatus.Free ? "TRANSPARENT" : "OPAQUE";
        if (appointment.Categories.Count > 0)
        {
            master.SetCategories(appointment.Categories);
        }

        if (appointment.SenderEmailAddress is { } organizer && appointment.IsMeeting)
        {
            master.Organizer = new CalendarAttendee(appointment.SenderSmtpAddress ?? organizer, appointment.SenderName);
        }

        foreach (var recipient in appointment.Recipients)
        {
            if (recipient.EmailAddress is { } address && recipient.RecipientType is PstRecipientType.To or PstRecipientType.Cc)
            {
                master.AddAttendee(new CalendarAttendee(recipient.SmtpAddress ?? address, recipient.DisplayName, recipient.RecipientType == PstRecipientType.Cc ? "OPT-PARTICIPANT" : "REQ-PARTICIPANT"));
            }
        }

        if (appointment.ReminderSet && appointment.ReminderDelta is { } delta)
        {
            var alarm = new CalendarComponent("VALARM");
            alarm.SetValue("ACTION", "DISPLAY");
            alarm.SetText("DESCRIPTION", appointment.Subject ?? "Reminder");
            alarm.SetValue("TRIGGER", CalendarDuration.Format(-delta));
            master.Components.Add(alarm);
            alarm.Calendar = calendar;
        }

        if (appointment.GetRecurrence() is not { } pattern || appointment.Start is not { } start)
        {
            return master;
        }

        // The pattern is in local times: the offset of the first occurrence converts them to UTC (the changes of daylight saving time are not known)
        var offset = pattern.StartDate + pattern.StartTimeOffset - start;
        if (ToRule(pattern, offset) is { } rule)
        {
            master.RecurrenceRule = rule;
        }

        var modified = pattern.Exceptions.Select(exception => exception.OriginalStartDate.Date).ToHashSet();
        foreach (var deleted in pattern.DeletedInstanceDates)
        {
            if (!modified.Contains(deleted.Date))
            {
                master.AddExceptionDate(appointment.IsAllDay ? new CalendarDateTime(deleted, isDate: true) : CalendarDateTime.FromUtc(DateTime.SpecifyKind(deleted.Date + pattern.StartTimeOffset - offset, DateTimeKind.Utc)));
            }
        }

        foreach (var exception in pattern.Exceptions)
        {
            var occurrence = calendar.Add(new VEvent());
            occurrence.Uid = uid;
            occurrence.Stamp = master.Stamp;
            occurrence.RecurrenceId = appointment.IsAllDay ? new CalendarDateTime(exception.OriginalStartDate, isDate: true) : CalendarDateTime.FromUtc(DateTime.SpecifyKind(exception.OriginalStartDate - offset, DateTimeKind.Utc));
            occurrence.Summary = exception.Subject ?? appointment.Subject;
            occurrence.Location = exception.Location ?? appointment.Location;
            SetTimes(occurrence, DateTime.SpecifyKind(exception.StartDateTime - offset, DateTimeKind.Utc), DateTime.SpecifyKind(exception.EndDateTime - offset, DateTimeKind.Utc), appointment.IsAllDay);
        }

        return master;
    }

    public static PstAppointmentBuilder ToAppointment(VEvent ev)
    {
        var builder = new PstAppointmentBuilder
        {
            Subject = ev.Summary,
            Body = ev.Description,
            Location = ev.Location,
            IsAllDay = ev.IsAllDay,
            BusyStatus = string.Equals(ev.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase) ? PstBusyStatus.Free : PstBusyStatus.Busy,
        };

        var start = ev.Start;
        var startUtc = start?.ToUtc(ev.Calendar) ?? start?.Value;
        var endUtc = ev.End is { } end ? end.ToUtc(ev.Calendar) ?? end.Value : ev.Duration is { } duration && startUtc is { } s ? s + duration : startUtc is { } single && ev.IsAllDay ? single.AddDays(1) : startUtc;
        builder.Start = startUtc is { } a ? DateTime.SpecifyKind(a, DateTimeKind.Utc) : null;
        builder.End = endUtc is { } b ? DateTime.SpecifyKind(b, DateTimeKind.Utc) : null;
        if (ev.Categories.Count > 0)
        {
            builder.Categories = ev.Categories;
        }

        if (ev.Organizer is { } organizer)
        {
            builder.SenderName = organizer.Name;
            builder.SenderEmailAddress = organizer.Address;
            builder.SenderAddressType = "SMTP";
        }

        foreach (var attendee in ev.Attendees)
        {
            builder.Recipients.Add(new PstRecipientBuilder(attendee.Name, attendee.Address, string.Equals(attendee.Role, "OPT-PARTICIPANT", StringComparison.OrdinalIgnoreCase) ? PstRecipientType.Cc : PstRecipientType.To));
        }

        foreach (var alarm in ev.Components.Where(component => component.Name == "VALARM"))
        {
            if (CalendarDuration.Parse(alarm.GetProperty("TRIGGER")?.Value) is { } trigger && trigger <= TimeSpan.Zero)
            {
                builder.Reminder = -trigger;
                break;
            }
        }

        if (start is { } first && ev.RecurrenceRule is { } rule && startUtc is { } firstUtc && endUtc is { } lastUtc)
        {
            builder.Recurrence = ToPattern(rule, first.Value, lastUtc - firstUtc, ev);
        }

        return builder;
    }

    public static VTodo AddTask(VCalendar calendar, PstTask task)
    {
        var todo = calendar.Add(new VTodo());
        todo.Uid = CreateUid(task);
        todo.SetDateTime("DTSTAMP", CalendarDateTime.FromUtc(task.LastModificationTime ?? task.CreationTime ?? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        todo.Summary = task.Subject;
        todo.Description = task.Body;
        todo.Start = task.StartDate is { } start ? CalendarDateTime.FromDate(DateOnly.FromDateTime(start.AddHours(12))) : null;
        todo.Due = task.DueDate is { } due ? CalendarDateTime.FromDate(DateOnly.FromDateTime(due.AddHours(12))) : null;
        todo.Completed = task.DateCompleted is { } completed ? CalendarDateTime.FromUtc(DateTime.SpecifyKind(completed, DateTimeKind.Utc)) : null;
        todo.PercentComplete = (int)Math.Round(Math.Clamp(task.PercentComplete, 0, 1) * 100, MidpointRounding.AwayFromZero);
        todo.Status = task.Status switch
        {
            PstTaskStatus.InProgress => "IN-PROCESS",
            PstTaskStatus.Complete => "COMPLETED",
            _ => "NEEDS-ACTION",
        };

        todo.Priority = task.Importance switch { PstImportance.High => 1, PstImportance.Low => 9, _ => 5 };
        return todo;
    }

    public static PstTaskBuilder ToTask(VTodo todo)
    {
        var builder = new PstTaskBuilder
        {
            Subject = todo.Summary,
            Body = todo.Description,
            StartDate = todo.Start is { } start ? DateTime.SpecifyKind(start.ToUtc(todo.Calendar) ?? start.Value, DateTimeKind.Utc) : null,
            DueDate = todo.Due is { } due ? DateTime.SpecifyKind(due.ToUtc(todo.Calendar) ?? due.Value, DateTimeKind.Utc) : null,
            DateCompleted = todo.Completed is { } completed ? DateTime.SpecifyKind(completed.ToUtc(todo.Calendar) ?? completed.Value, DateTimeKind.Utc) : null,
            PercentComplete = Math.Clamp(todo.PercentComplete ?? 0, 0, 100) / 100.0,
            Status = todo.Status?.ToUpperInvariant() switch
            {
                "IN-PROCESS" => PstTaskStatus.InProgress,
                "COMPLETED" => PstTaskStatus.Complete,
                _ => PstTaskStatus.NotStarted,
            },
            Importance = todo.Priority switch { >= 1 and <= 4 => PstImportance.High, >= 6 and <= 9 => PstImportance.Low, _ => PstImportance.Normal },
        };

        return builder;
    }

    /// <summary>Returns the rule of a pattern, or <see langword="null"/> for a pattern of the Hijri calendar.</summary>
    internal static RecurrenceRule? ToRule(PstRecurrencePattern pattern, TimeSpan offset)
    {
        var rule = new RecurrenceRule { WeekStart = pattern.FirstDayOfWeek };
        var yearly = pattern.Frequency == PstRecurrenceFrequency.Yearly;
        switch (pattern.PatternType)
        {
            case PstRecurrencePatternType.Day:
                rule.Frequency = RecurrenceFrequency.Daily;
                rule.Interval = Math.Max(1, pattern.Period / 1440);
                break;
            case PstRecurrencePatternType.Week:
                rule.Frequency = RecurrenceFrequency.Weekly;
                rule.Interval = Math.Max(1, pattern.Period);
                foreach (var day in GetDays(pattern.DaysOfWeek))
                {
                    rule.ByDay.Add(new RecurrenceDay(day));
                }

                break;
            case PstRecurrencePatternType.Month or PstRecurrencePatternType.MonthEnd or PstRecurrencePatternType.MonthNth:
                rule.Frequency = yearly ? RecurrenceFrequency.Yearly : RecurrenceFrequency.Monthly;
                rule.Interval = Math.Max(1, yearly ? pattern.Period / 12 : pattern.Period);
                if (yearly)
                {
                    rule.ByMonth.Add(pattern.StartDate.Month);
                }

                if (pattern.PatternType == PstRecurrencePatternType.MonthNth)
                {
                    foreach (var day in GetDays(pattern.DaysOfWeek))
                    {
                        rule.ByDay.Add(new RecurrenceDay(day));
                    }

                    rule.BySetPosition.Add(pattern.WeekOfMonth >= 5 ? -1 : Math.Max(1, pattern.WeekOfMonth));
                }
                else
                {
                    rule.ByMonthDay.Add(pattern.PatternType == PstRecurrencePatternType.MonthEnd ? -1 : Math.Clamp(pattern.DayOfMonth, 1, 31));
                }

                break;
            default:
                return null;
        }

        switch (pattern.EndType)
        {
            case PstRecurrenceEndType.EndAfterOccurrences:
                rule.Count = Math.Max(1, pattern.OccurrenceCount);
                break;
            case PstRecurrenceEndType.EndAfterDate:
                rule.Until = CalendarDateTime.FromUtc(DateTime.SpecifyKind(pattern.EndDate.Date + pattern.StartTimeOffset - offset, DateTimeKind.Utc));
                break;
        }

        return rule;
    }

    /// <summary>Returns the pattern of a rule, or <see langword="null"/> when Outlook cannot represent the rule.</summary>
    private static PstRecurrencePattern? ToPattern(RecurrenceRule rule, DateTime start, TimeSpan duration, VEvent ev)
    {
        if (rule.OtherParts.Count > 0 || rule.BySetPosition.Count > 1)
        {
            return null;
        }

        var pattern = new PstRecurrencePattern
        {
            StartDate = start.Date,
            StartTimeOffset = start.TimeOfDay,
            EndTimeOffset = start.TimeOfDay + duration,
            FirstDayOfWeek = rule.WeekStart,
        };

        var days = rule.ByDay.Aggregate(PstDaysOfWeek.None, (current, day) => current | (PstDaysOfWeek)(1 << (int)day.Day));
        switch (rule.Frequency)
        {
            case RecurrenceFrequency.Daily when rule.ByDay.Count == 0 && rule.ByMonth.Count == 0 && rule.ByMonthDay.Count == 0:
                pattern.Frequency = PstRecurrenceFrequency.Daily;
                pattern.PatternType = PstRecurrencePatternType.Day;
                pattern.Period = rule.Interval * 1440;
                pattern.FirstDateTime = (int)((long)(start.Date - Origin).TotalMinutes % pattern.Period);
                break;
            case RecurrenceFrequency.Weekly when rule.ByMonth.Count == 0 && rule.ByMonthDay.Count == 0 && rule.ByDay.All(day => day.Position == 0):
                pattern.Frequency = PstRecurrenceFrequency.Weekly;
                pattern.PatternType = PstRecurrencePatternType.Week;
                pattern.Period = rule.Interval;
                pattern.DaysOfWeek = days == PstDaysOfWeek.None ? (PstDaysOfWeek)(1 << (int)start.DayOfWeek) : days;
                var weekStart = start.Date.AddDays(-(((int)start.DayOfWeek - (int)rule.WeekStart + 7) % 7));
                pattern.FirstDateTime = (int)((long)(weekStart - Origin).TotalMinutes % (pattern.Period * 10080L));
                break;
            case RecurrenceFrequency.Monthly or RecurrenceFrequency.Yearly:
                {
                    var yearly = rule.Frequency == RecurrenceFrequency.Yearly;
                    if (yearly && (rule.ByMonth.Count > 1 || (rule.ByMonth.Count == 1 && rule.ByMonth[0] != start.Month)))
                    {
                        return null;
                    }

                    if (!yearly && rule.ByMonth.Count > 0)
                    {
                        return null;
                    }

                    pattern.Frequency = yearly ? PstRecurrenceFrequency.Yearly : PstRecurrenceFrequency.Monthly;
                    pattern.Period = yearly ? rule.Interval * 12 : rule.Interval;
                    if (rule.ByDay.Count > 0)
                    {
                        // The nth day: BYDAY=2MO, or BYDAY=MO,TU;BYSETPOS=2
                        var positions = rule.ByDay.Select(day => day.Position).Distinct().ToList();
                        var position = positions is [0] && rule.BySetPosition.Count == 1 ? rule.BySetPosition[0] : positions is [var single] && single != 0 && rule.BySetPosition.Count == 0 ? single : 0;
                        if (position is 0 or < -1 or > 4 || rule.ByMonthDay.Count > 0)
                        {
                            return null;
                        }

                        pattern.PatternType = PstRecurrencePatternType.MonthNth;
                        pattern.DaysOfWeek = days;
                        pattern.WeekOfMonth = position == -1 ? 5 : position;
                    }
                    else if (rule.ByMonthDay is [-1])
                    {
                        pattern.PatternType = PstRecurrencePatternType.MonthEnd;
                        pattern.DayOfMonth = 31;
                    }
                    else if (rule.ByMonthDay.Count <= 1 && rule.BySetPosition.Count == 0)
                    {
                        var day = rule.ByMonthDay.Count == 1 ? rule.ByMonthDay[0] : start.Day;
                        if (day < 1)
                        {
                            return null;
                        }

                        pattern.PatternType = PstRecurrencePatternType.Month;
                        pattern.DayOfMonth = day;
                    }
                    else
                    {
                        return null;
                    }

                    var months = ((start.Year - 1601) * 12) + start.Month - 1;
                    pattern.FirstDateTime = (int)(Origin.AddMonths(months % pattern.Period) - Origin).TotalMinutes;
                    break;
                }

            default:
                return null;
        }

        if (rule.Count is { } count)
        {
            pattern.EndType = PstRecurrenceEndType.EndAfterOccurrences;
            pattern.OccurrenceCount = count;
            pattern.EndDate = GetOccurrences(ev).Take(count).LastOrDefault(start).Date;
        }
        else if (rule.Until is { } until)
        {
            pattern.EndType = PstRecurrenceEndType.EndAfterDate;
            pattern.EndDate = GetOccurrences(ev).TakeWhile(date => date <= (until.IsDate ? until.Value.Date.AddDays(1) : until.Value)).Take(100_000).LastOrDefault(start).Date;
            pattern.OccurrenceCount = 10;
        }
        else
        {
            pattern.EndType = PstRecurrenceEndType.NeverEnd;
        }

        foreach (var exception in ev.ExceptionDates)
        {
            pattern.DeletedInstanceDates.Add(exception.Value.Date);
        }

        return pattern;
    }

    private static IEnumerable<DateTime> GetOccurrences(VEvent ev)
    {
        try
        {
            return [.. ev.GetOccurrences().Take(100_000)];
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private static IEnumerable<DayOfWeek> GetDays(PstDaysOfWeek days)
    {
        for (var day = DayOfWeek.Sunday; day <= DayOfWeek.Saturday; day++)
        {
            if (((int)days & (1 << (int)day)) != 0)
            {
                yield return day;
            }
        }
    }

    private static void SetTimes(VEvent ev, DateTime? start, DateTime? end, bool isAllDay)
    {
        if (isAllDay)
        {
            // An all-day appointment starts at midnight in the time zone of its writer: noon in UTC is in the same day for the offsets of less than 12 hours
            ev.Start = start is { } s ? CalendarDateTime.FromDate(DateOnly.FromDateTime(s.AddHours(12))) : null;
            ev.End = end is { } e ? CalendarDateTime.FromDate(DateOnly.FromDateTime(e.AddHours(12))) : null;
        }
        else
        {
            ev.Start = start is { } s ? CalendarDateTime.FromUtc(DateTime.SpecifyKind(s, DateTimeKind.Utc)) : null;
            ev.End = end is { } e ? CalendarDateTime.FromUtc(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : null;
        }
    }

    private static void AddPhone(VCard card, string? number, params string[] types)
    {
        if (!string.IsNullOrEmpty(number))
        {
            card.Phones.Add(new VCardValue(number, types));
        }
    }

    private static void AddAddress(VCard card, PstPostalAddress address, string type)
    {
        if (address.IsEmpty)
        {
            return;
        }

        var result = new VCardAddress { Street = address.Street, Locality = address.City, Region = address.StateOrProvince, PostalCode = address.PostalCode, Country = address.Country };
        result.Types.Add(type);
        card.Addresses.Add(result);
    }

    /// <summary>Returns a unique identifier derived from the item, so that the same item converts to the same identifier.</summary>
    private static string CreateUid(PstMessage item)
    {
        var key = string.Join("|", item.InternetMessageId, item.Subject, item.CreationTime?.Ticks, item.NodeId, item.MessageClass);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + "@meziantou.framework.mail";
    }
}
