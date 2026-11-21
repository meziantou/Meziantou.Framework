using System.Text;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Calendar;

/// <summary>An iCalendar object ([RFC 5545], <c>.ics</c>): its events, to-dos, time zones and the other components.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Email.Tests/CalendarTests.cs" ref:region="VCalendar_Usage" ref:dedent="true">
/// var calendar = VCalendar.Load(path).Single();
/// var meeting = calendar.Events.First();
/// Assert.Equal("Weekly meeting", meeting.Summary);
/// Assert.Equal("Room 1; building A", meeting.Location);
///
/// // The start is 9:00 in Paris: the time zone of the calendar converts it to UTC
/// Assert.Equal(new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc), meeting.Start!.Value.ToUtc(calendar));
/// Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), meeting.GetOccurrences().First());
///
/// var created = new VCalendar();
/// var ev = created.Add(new VEvent
/// {
///     Uid = "launch@example.com",
///     Summary = "Launch",
///     Start = CalendarDateTime.FromUtc(new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc)),
///     Duration = TimeSpan.FromHours(1),
///     RecurrenceRule = RecurrenceRule.Parse("FREQ=WEEKLY;COUNT=4"),
/// });
/// var text = created.ToString();
/// </code>
/// </example>
public sealed class VCalendar : CalendarComponent
{
    private const long MaxSize = 64 * 1024 * 1024;
    private const int MaxDepth = 32;

    /// <summary>Creates a calendar with the properties <c>VERSION:2.0</c> and <c>PRODID</c>.</summary>
    public VCalendar()
        : this(addDefaults: true)
    {
    }

    internal VCalendar(bool addDefaults)
        : base("VCALENDAR")
    {
        Calendar = this;
        if (addDefaults)
        {
            Properties.Add(new ContentLine("VERSION", "2.0"));
            Properties.Add(new ContentLine("PRODID", "-//Meziantou//Meziantou.Framework.Email//EN"));
        }
    }

    /// <summary>Gets or sets the identifier of the product that wrote the calendar (<c>PRODID</c>).</summary>
    public string? ProductId
    {
        get => GetText("PRODID");
        set => SetText("PRODID", value);
    }

    /// <summary>Gets or sets the method of an iTIP message (<c>METHOD</c>), such as <c>REQUEST</c>.</summary>
    public string? Method
    {
        get => GetProperty("METHOD")?.Value;
        set => SetValue("METHOD", value);
    }

    /// <summary>Gets the events.</summary>
    public IEnumerable<VEvent> Events => GetComponents<VEvent>();

    /// <summary>Gets the to-dos.</summary>
    public IEnumerable<VTodo> Todos => GetComponents<VTodo>();

    /// <summary>Gets the time zones.</summary>
    public IEnumerable<VTimeZone> TimeZones => GetComponents<VTimeZone>();

    /// <summary>Returns the definition of a time zone.</summary>
    /// <param name="id">The identifier of the time zone (<c>TZID</c>).</param>
    /// <returns>The time zone, or <see langword="null"/> when the calendar does not define it.</returns>
    public VTimeZone? FindTimeZone(string id) => TimeZones.FirstOrDefault(timeZone => string.Equals(timeZone.Id, id, StringComparison.Ordinal));

    /// <summary>Adds a component, such as an event.</summary>
    /// <typeparam name="T">The type of the component.</typeparam>
    /// <param name="component">The component.</param>
    /// <returns>The component.</returns>
    public T Add<T>(T component)
        where T : CalendarComponent
    {
        ArgumentNullException.ThrowIfNull(component);
        Components.Add(component);
        SetCalendar(component, this);
        return component;
    }

    /// <summary>Adds an appointment of Outlook: an event, with its recurrence rule, the dates of its deleted occurrences (<c>EXDATE</c>), and an event per modified occurrence (<c>RECURRENCE-ID</c>). The times are in UTC.</summary>
    /// <param name="appointment">The appointment.</param>
    /// <returns>The event of the appointment.</returns>
    public VEvent AddAppointment(MapiAppointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        return CalendarConverter.AddAppointment(this, appointment);
    }

    /// <summary>Adds a task of Outlook as a to-do.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The to-do.</returns>
    public VTodo AddTask(MapiTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return CalendarConverter.AddTask(this, task);
    }

    /// <summary>Reads the calendars of a text (a file usually has one).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The calendars.</returns>
    /// <exception cref="InvalidEmailDataException">The components are nested deeper than 32 levels.</exception>
    public static IReadOnlyList<VCalendar> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new List<VCalendar>();
        var stack = new Stack<CalendarComponent>();
        foreach (var line in ContentLineReader.Parse(text))
        {
            if (line.Name == "BEGIN")
            {
                var name = line.Value.Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                if (stack.Count >= MaxDepth)
                {
                    throw new InvalidEmailDataException("The components of the iCalendar object are nested too deeply.");
                }

                var component = CalendarComponent.Create(name);
                if (stack.Count == 0)
                {
                    if (component is not VCalendar)
                    {
                        // A component outside a calendar is ignored, with its content
                        stack.Push(component);
                        continue;
                    }
                }
                else
                {
                    stack.Peek().Components.Add(component);
                }

                stack.Push(component);
                continue;
            }

            if (line.Name == "END")
            {
                var name = line.Value.Trim();
                if (stack.Count > 0 && string.Equals(stack.Peek().Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    var component = stack.Pop();
                    if (stack.Count == 0 && component is VCalendar calendar)
                    {
                        SetCalendar(calendar, calendar);
                        result.Add(calendar);
                    }
                }

                continue;
            }

            if (stack.Count > 0)
            {
                stack.Peek().Properties.Add(line);
            }
        }

        // A calendar without its END line is kept
        if (stack.Count > 0 && stack.Last() is VCalendar unclosed)
        {
            SetCalendar(unclosed, unclosed);
            result.Add(unclosed);
        }

        return result;
    }

    /// <summary>Reads the calendars of a file, in UTF-8 (or Latin-1 when the bytes are not UTF-8).</summary>
    /// <param name="data">The content of the file.</param>
    /// <returns>The calendars.</returns>
    /// <exception cref="InvalidEmailDataException">The components are nested deeper than 32 levels.</exception>
    public static IReadOnlyList<VCalendar> Load(ReadOnlySpan<byte> data) => Parse(ContentLineReader.DecodeFile(data));

    /// <summary>Reads the calendars of a stream, until its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The calendars.</returns>
    /// <exception cref="InvalidEmailDataException">The components are nested deeper than 32 levels.</exception>
    /// <exception cref="EmailResourceLimitException">The content is larger than 64 MB.</exception>
    public static IReadOnlyList<VCalendar> Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Load(StreamHelper.ReadAll(stream, MaxSize));
    }

    /// <summary>Reads the calendars of an <c>.ics</c> file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The calendars.</returns>
    /// <exception cref="InvalidEmailDataException">The components are nested deeper than 32 levels.</exception>
    /// <exception cref="EmailResourceLimitException">The content is larger than 64 MB.</exception>
    public static IReadOnlyList<VCalendar> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Writes the calendar in an <c>.ics</c> file, in UTF-8.</summary>
    /// <param name="path">The path of the file.</param>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        File.WriteAllText(path, ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void SetCalendar(CalendarComponent component, VCalendar calendar)
    {
        component.Calendar = calendar;
        foreach (var child in component.Components)
        {
            SetCalendar(child, calendar);
        }
    }
}
