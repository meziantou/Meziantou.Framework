using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Components;

/// <summary>An input component for editing date and time values using the HTML datetime-local input type.</summary>
/// <typeparam name="TValue">The type of the value. Supported types are <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, and their nullable variants.</typeparam>
public class InputDateTime<TValue> : InputDate<TValue>
{
    // datetime-local has no seconds component, so values round-tripped through this input are truncated to the minute
    private const string DateFormat = "yyyy-MM-ddTHH:mm";

    /// <summary>
    /// Gets or sets the UTC offset to apply when the bound value is a <see cref="DateTimeOffset"/>.
    /// An HTML <c>datetime-local</c> input carries no timezone, so without this the offset of the machine running the
    /// component is used, which is the server's offset under Blazor Server rather than the user's.
    /// Use <see cref="TimeZoneService"/> to obtain the user's offset.
    /// </summary>
    [Parameter]
    public TimeSpan? Offset { get; set; }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "type", "datetime-local");
        builder.AddAttribute(3, "class", CssClass);
        builder.AddAttribute(4, "value", BindConverter.FormatValue(CurrentValueAsString));
        builder.AddAttribute(5, "onchange", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.CloseElement();
    }

    /// <inheritdoc />
    protected override string FormatValueAsString(TValue? value)
    {
        return value switch
        {
            DateTime dateTimeValue => BindConverter.FormatValue(dateTimeValue, DateFormat, CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffsetValue => BindConverter.FormatValue(dateTimeOffsetValue, DateFormat, CultureInfo.InvariantCulture),
            _ => "",// Handles null for Nullable<DateTime>, etc.
        };
    }

    /// <inheritdoc />
    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        // Unwrap nullable types. We don't have to deal with receiving empty values for nullable
        // types here, because the underlying InputBase already covers that.
        var targetType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

        bool success;
        if (targetType == typeof(DateTime))
        {
            success = TryParseDateTime(value, out result);
        }
        else if (targetType == typeof(DateTimeOffset))
        {
            success = TryParseDateTimeOffset(value, Offset, out result);
        }
        else
        {
            throw new InvalidOperationException($"The type '{targetType}' is not a supported date type.");
        }

        if (success)
        {
            Debug.Assert(result is not null);
            validationErrorMessage = null;
            return true;
        }
        else
        {
            validationErrorMessage = string.Format(CultureInfo.CurrentCulture, ParsingErrorMessage, FieldIdentifier.FieldName);
            return false;
        }
    }

    private static bool TryParseDateTime(string? value, out TValue? result)
    {
        var success = BindConverter.TryConvertToDateTime(value, CultureInfo.InvariantCulture, DateFormat, out var parsedValue);
        if (success)
        {
            result = (TValue)(object)parsedValue;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    private static bool TryParseDateTimeOffset(string? value, TimeSpan? offset, out TValue? result)
    {
        var success = BindConverter.TryConvertToDateTimeOffset(value, CultureInfo.InvariantCulture, DateFormat, out var parsedValue);
        if (success)
        {
            if (offset is { } userOffset)
            {
                // Reinterpret the wall-clock time the user typed as being in their timezone rather than the machine's
                parsedValue = new DateTimeOffset(DateTime.SpecifyKind(parsedValue.DateTime, DateTimeKind.Unspecified), userOffset);
            }

            result = (TValue)(object)parsedValue;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }
}
