using System.Globalization;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// The editable fields of the date and time controls (<input type=date|month|week|time|datetime-local>), as in the browsers
// (the HTML Standard leaves their user interface to the user agent): the value is edited field by field (the month, the
// day, the year, the week, the hour, the minute, AM or PM, in the order of the en-US format of the control). The digits
// typed in the focused field replace its value, and the focus moves to the next field once the field cannot take another
// digit; Tab and Shift+Tab move the focus between the fields before leaving the control, the left and right arrow keys move
// it too, the up and down arrow keys step the focused field, and Backspace or Delete clears it. The value of the control is
// set when every field has a value (the empty string otherwise): input and change are then fired, and the user validity of
// the control becomes true. The seconds and the fractions of a second of a time are not edited: they are kept when another
// field is edited.
public partial class HtmlInputElement
{
    private DateTimeFields? _dateTimeFields;

    private enum DateTimeFieldKind : byte
    {
        Month,
        Day,
        Year,
        Week,
        Hour,
        Minute,
        Meridiem,
    }

    /// <summary>Whether the control is a date and time control, whose value is edited field by field.</summary>
    internal bool IsDateTimeControl => _type is InputType.Date or InputType.Month or InputType.Week or InputType.Time or InputType.DateTimeLocal;

    /// <summary>The fields of a date and time control, and the value of the control they were last synchronized with.</summary>
    private sealed class DateTimeFields(InputType type, DateTimeFieldKind[] kinds, int?[] values, string syncedValue, string timeSuffix)
    {
        public InputType Type { get; } = type;

        public DateTimeFieldKind[] Kinds { get; } = kinds;

        public int?[] Values { get; } = values;

        /// <summary>The value of the control the fields were synchronized with, or null when they must be synchronized again.</summary>
        public string? SyncedValue { get; set; } = syncedValue;

        /// <summary>The seconds and the fractions of a second of the time of the value (e.g. <c>:56.789</c>), kept by the edits.</summary>
        public string TimeSuffix { get; } = timeSuffix;

        /// <summary>The index of the focused field.</summary>
        public int Focused { get; set; }

        /// <summary>The number of digits typed in the focused field since it got the focus.</summary>
        public int TypedDigits { get; set; }
    }

    /// <summary>
    /// The default action of the trusted <c>keydown</c> event of the keys that move between the fields of a date and time
    /// control (Tab, Shift+Tab, the left and right arrow keys) or step its focused field (the up and down arrow keys). Returns
    /// false when the key does nothing in the control (e.g. Tab in its last field, which moves the focus to the next control).
    /// </summary>
    internal static bool RunDateTimeKeyDefaultAction(KeyboardEvent @event, Node target)
    {
        if (target is not HtmlInputElement { IsDateTimeControl: true } input || @event.CtrlKey || @event.AltKey || @event.MetaKey)
            return false;

        var fields = input.GetDateTimeFields();
        switch (@event.Key)
        {
            case "Tab":
                var next = fields.Focused + (@event.ShiftKey ? -1 : 1);
                if (next < 0 || next >= fields.Kinds.Length)
                {
                    // The focus leaves the control: it gets the focus in its first field again
                    FocusDateTimeField(fields, 0);
                    return false;
                }

                FocusDateTimeField(fields, next);
                return true;

            case "ArrowLeft" or "ArrowRight":
                FocusDateTimeField(fields, Math.Clamp(fields.Focused + (@event.Key == "ArrowRight" ? 1 : -1), 0, fields.Kinds.Length - 1));
                return true;

            case "ArrowUp" or "ArrowDown":
                if (!input.IsMutable)
                    return true;

                var kind = fields.Kinds[fields.Focused];
                var (min, max, _) = GetDateTimeFieldRange(kind);
                var current = fields.Values[fields.Focused];
                var up = @event.Key == "ArrowUp";
                fields.Values[fields.Focused] = current is not { } value
                    ? GetDefaultDateTimeFieldValue(input, kind, min, max, up)
                    : up ? (value >= max ? min : value + 1) : (value <= min ? max : value - 1);
                fields.TypedDigits = 0;
                input.CommitDateTimeFields(fields);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// An edit of the user in the focused field of a date and time control (see <see cref="TextControlEditing.UserEdit"/>): the
    /// typed digits (or <c>a</c> and <c>p</c> in the AM/PM field), and the deletions, which clear the field. Returns false when
    /// nothing is edited.
    /// </summary>
    internal bool EditDateTimeFieldsByUser(string inputType, string? data)
    {
        if (!IsDateTimeControl || !IsMutable || !IsConnected || NodeDocument.BrowsingContext is null)
            return false;

        var fields = GetDateTimeFields();
        switch (inputType)
        {
            case "deleteContentBackward" or "deleteContentForward" or "deleteWordBackward" or "deleteWordForward" or "deleteByCut":
                if (fields.Values[fields.Focused] is null)
                    return false;

                fields.Values[fields.Focused] = null;
                fields.TypedDigits = 0;
                CommitDateTimeFields(fields);
                return true;

            case "insertText" or "insertFromPaste" or "insertReplacementText" when !string.IsNullOrEmpty(data):
                var edited = false;
                foreach (var c in data)
                {
                    edited |= TypeInDateTimeField(fields, c);
                }

                if (edited)
                {
                    CommitDateTimeFields(fields);
                }

                return edited;

            default:
                return false;
        }
    }

    /// <summary>The steps run when the control loses the focus: the next time it gets the focus, its first field is focused.</summary>
    internal void OnDateTimeControlUnfocused()
    {
        if (_dateTimeFields is { } fields)
        {
            FocusDateTimeField(fields, 0);
        }
    }

    private static bool TypeInDateTimeField(DateTimeFields fields, char c)
    {
        var kind = fields.Kinds[fields.Focused];
        if (kind == DateTimeFieldKind.Meridiem)
        {
            int? meridiem = char.ToLowerInvariant(c) switch
            {
                'a' => 0,
                'p' => 1,
                _ => null,
            };

            if (meridiem is null)
                return false;

            fields.Values[fields.Focused] = meridiem;
            return true;
        }

        if (!char.IsAsciiDigit(c))
            return false;

        // The first digit typed in a field replaces its value, the next ones are appended to it
        var (min, max, digits) = GetDateTimeFieldRange(kind);
        var digit = c - '0';
        var value = fields.TypedDigits == 0 ? digit : (fields.Values[fields.Focused] ?? 0) * 10 + digit;
        fields.TypedDigits++;
        if (value > max)
        {
            // The digit starts a new value
            value = digit;
            fields.TypedDigits = 1;
        }

        fields.Values[fields.Focused] = value;

        // The field is complete when another digit would exceed its maximum: the next field gets the focus
        var complete = fields.TypedDigits >= digits || (value * 10) > max;
        if (complete && value >= min && fields.Focused + 1 < fields.Kinds.Length)
        {
            FocusDateTimeField(fields, fields.Focused + 1);
        }

        return true;
    }

    private static void FocusDateTimeField(DateTimeFields fields, int index)
    {
        fields.Focused = index;
        fields.TypedDigits = 0;
    }

    /// <summary>The minimum, the maximum and the maximum number of digits of a field.</summary>
    private static (int Min, int Max, int Digits) GetDateTimeFieldRange(DateTimeFieldKind kind) => kind switch
    {
        DateTimeFieldKind.Month => (1, 12, 2),
        DateTimeFieldKind.Day => (1, 31, 2),
        DateTimeFieldKind.Year => (1, 275760, 6),
        DateTimeFieldKind.Week => (1, 53, 2),
        DateTimeFieldKind.Hour => (1, 12, 2),
        DateTimeFieldKind.Minute => (0, 59, 2),
        _ => (0, 1, 1),
    };

    /// <summary>The value of an empty field stepped by an arrow key: the current year, else the minimum (up) or the maximum (down).</summary>
    private static int GetDefaultDateTimeFieldValue(HtmlInputElement input, DateTimeFieldKind kind, int min, int max, bool up)
    {
        if (kind == DateTimeFieldKind.Year)
            return (input.NodeDocument.DefaultView?.EventLoop.TimeProvider ?? TimeProvider.System).GetLocalNow().Year;

        return up ? min : max;
    }

    /// <summary>
    /// The value is set by a script, by the reset algorithm or by the <c>value</c> attribute: the fields are synchronized
    /// with it the next time they are used, even when it is the empty string of fields that are partially filled.
    /// </summary>
    private void InvalidateDateTimeFields()
    {
        if (_dateTimeFields is { } fields)
        {
            fields.SyncedValue = null;
        }
    }

    /// <summary>The fields of the control, synchronized with its value when the value changed since they were last edited.</summary>
    private DateTimeFields GetDateTimeFields()
    {
        if (_dateTimeFields is { } fields && fields.Type == _type && fields.SyncedValue == _value)
            return fields;

        var kinds = _type switch
        {
            InputType.Date => new[] { DateTimeFieldKind.Month, DateTimeFieldKind.Day, DateTimeFieldKind.Year },
            InputType.Month => [DateTimeFieldKind.Month, DateTimeFieldKind.Year],
            InputType.Week => [DateTimeFieldKind.Week, DateTimeFieldKind.Year],
            InputType.Time => [DateTimeFieldKind.Hour, DateTimeFieldKind.Minute, DateTimeFieldKind.Meridiem],
            _ => [DateTimeFieldKind.Month, DateTimeFieldKind.Day, DateTimeFieldKind.Year, DateTimeFieldKind.Hour, DateTimeFieldKind.Minute, DateTimeFieldKind.Meridiem],
        };

        var values = new int?[kinds.Length];
        ParseDateTimeFields(_type, _value, kinds, values, out var timeSuffix);
        var result = new DateTimeFields(_type, kinds, values, _value, timeSuffix);
        if (_dateTimeFields is { } previous && previous.Type == _type)
        {
            result.Focused = previous.Focused;
        }

        _dateTimeFields = result;
        return result;
    }

    private static void ParseDateTimeFields(InputType type, string value, DateTimeFieldKind[] kinds, int?[] values, out string timeSuffix)
    {
        timeSuffix = "";
        if (value.Length == 0)
            return;

        int? year = null, month = null, day = null, week = null, hour = null, minute = null;
        switch (type)
        {
            case InputType.Date when Microsyntaxes.TryParseDateString(value, out var y, out var m, out var d):
                (year, month, day) = (y, m, d);
                break;

            case InputType.Month when Microsyntaxes.TryParseMonthString(value, out var y, out var m):
                (year, month) = (y, m);
                break;

            case InputType.Week when Microsyntaxes.TryParseWeekString(value, out var y, out var w):
                (year, week) = (y, w);
                break;

            case InputType.Time when Microsyntaxes.TryParseTimeString(value, out var h, out var min, out _):
                (hour, minute) = (h, min);
                timeSuffix = GetTimeSuffix(value);
                break;

            case InputType.DateTimeLocal:
                var separator = value.IndexOfAny('T', ' ');
                if (separator > 0 && Microsyntaxes.TryParseDateString(value.AsSpan(0, separator), out var dy, out var dm, out var dd)
                    && Microsyntaxes.TryParseTimeString(value.AsSpan(separator + 1), out var th, out var tm, out _))
                {
                    (year, month, day, hour, minute) = (dy, dm, dd, th, tm);
                    timeSuffix = GetTimeSuffix(value[(separator + 1)..]);
                }

                break;
        }

        for (var i = 0; i < kinds.Length; i++)
        {
            values[i] = kinds[i] switch
            {
                DateTimeFieldKind.Month => month,
                DateTimeFieldKind.Day => day,
                DateTimeFieldKind.Year => year is <= 275760 ? year : null,
                DateTimeFieldKind.Week => week,
                DateTimeFieldKind.Hour => hour is { } h ? (h % 12 == 0 ? 12 : h % 12) : null,
                DateTimeFieldKind.Minute => minute,
                _ => hour is { } h ? (h >= 12 ? 1 : 0) : null,
            };
        }
    }

    /// <summary>The seconds and the fractions of a second of a valid time string (<c>HH:MM</c>, then <c>:SS</c> and <c>.fff</c>).</summary>
    private static string GetTimeSuffix(string time) => time.Length > 5 ? time[5..] : "";

    /// <summary>The value of the fields: the empty string when a field has no value.</summary>
    private static string FormatDateTimeFields(DateTimeFields fields)
    {
        int year = 0, month = 0, day = 0, week = 0, hour = 0, minute = 0, meridiem = 0;
        for (var i = 0; i < fields.Kinds.Length; i++)
        {
            if (fields.Values[i] is not { } value)
                return "";

            switch (fields.Kinds[i])
            {
                case DateTimeFieldKind.Month: month = value; break;
                case DateTimeFieldKind.Day: day = value; break;
                case DateTimeFieldKind.Year: year = value; break;
                case DateTimeFieldKind.Week: week = value; break;
                case DateTimeFieldKind.Hour: hour = value; break;
                case DateTimeFieldKind.Minute: minute = value; break;
                default: meridiem = value; break;
            }
        }

        var hour24 = (hour % 12) + (meridiem * 12);
        var invariant = CultureInfo.InvariantCulture;
        return fields.Type switch
        {
            InputType.Date => string.Create(invariant, $"{year:D4}-{month:D2}-{day:D2}"),
            InputType.Month => string.Create(invariant, $"{year:D4}-{month:D2}"),
            InputType.Week => string.Create(invariant, $"{year:D4}-W{week:D2}"),
            InputType.Time => string.Create(invariant, $"{hour24:D2}:{minute:D2}{fields.TimeSuffix}"),
            _ => string.Create(invariant, $"{year:D4}-{month:D2}-{day:D2}T{hour24:D2}:{minute:D2}{fields.TimeSuffix}"),
        };
    }

    /// <summary>
    /// Sets the value of the control to the value of its fields (sanitized: an invalid date is the empty string). When the
    /// value changes, input and change are fired, and the user validity of the control becomes true.
    /// </summary>
    private void CommitDateTimeFields(DateTimeFields fields)
    {
        var value = FormatDateTimeFields(fields);
        var previous = _value;
        _value = value;
        _dirtyValue = true;
        _userInput = null;
        SanitizeValue();
        fields.SyncedValue = _value;
        var document = NodeDocument;
        document.IncrementElementStateVersion();
        if (_value == previous)
            return;

        var view = document.DefaultView;
        FireEvent(new InputEvent("input", new InputEventInit { Bubbles = true, Composed = true, InputType = "insertText", View = view }));
        if (!IsConnected || !document.IsFullyActive)
            return;

        UserValidity = true;
        FireEvent(new Event("change", new EventInit { Bubbles = true }));
    }
}
