using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Components;

/// <summary>A select input component for editing enum values.</summary>
/// <typeparam name="TEnum">The enum type. Can be nullable to allow no selection.</typeparam>
/// <remarks>
/// <para>
/// This component automatically generates option elements for each enum value. Display names can be customized
/// using the <see cref="DisplayAttribute"/> on enum members.
/// </para>
/// </remarks>
// Note that adding a constraint on TEnum (where T : Enum) doesn't work when used in the view, Razor raises an error at build time. Also, this would prevent using nullable types...
public sealed class InputEnumSelect<TEnum> : InputBase<TEnum>
{
    private static readonly bool IsNullable = Nullable.GetUnderlyingType(typeof(TEnum)) is not null;
    private static readonly (string Value, string? DisplayName)[] Options = GetOptions();

    /// <summary>Gets or sets the text of the empty option rendered for nullable enum types. Defaults to an empty string.</summary>
    [Parameter]
    public string EmptyOptionText { get; set; } = "";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "select");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "class", CssClass);
        builder.AddAttribute(3, "value", BindConverter.FormatValue(CurrentValueAsString));
        builder.AddAttribute(4, "onchange", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString, culture: null));

        // A nullable enum needs an empty option, otherwise null is not selectable and a null value silently displays
        // as the first member of the enum
        if (IsNullable)
        {
            builder.OpenElement(5, "option");
            builder.AddAttribute(6, "value", "");
            builder.AddContent(7, EmptyOptionText);
            builder.CloseElement();
        }

        // Add an option element per enum value
        foreach (var option in Options)
        {
            builder.OpenElement(8, "option");
            builder.AddAttribute(9, "value", option.Value);
            builder.AddContent(10, option.DisplayName);
            builder.CloseElement();
        }

        builder.CloseElement(); // close the select element
    }

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TEnum result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        // Let's Blazor convert the value for us 😊
        if (BindConverter.TryConvertTo(value, CultureInfo.CurrentCulture, out TEnum? parsedValue))
        {
            result = parsedValue!;
            validationErrorMessage = "";
            return true;
        }

        // Map null/empty value to null if the bound object is nullable
        if (string.IsNullOrEmpty(value))
        {
            var nullableType = Nullable.GetUnderlyingType(typeof(TEnum));
            if (nullableType != null)
            {
                result = default!;
                validationErrorMessage = "";
                return true;
            }
        }

        // The value is invalid => set the error message
        result = default;
        validationErrorMessage = $"The {FieldIdentifier.FieldName} field is not valid.";
        return false;
    }

    // Reflecting over the enum members on every render is wasteful: the set of options cannot change at runtime
    private static (string Value, string? DisplayName)[] GetOptions()
    {
        var values = Enum.GetValues(GetEnumType());
        var result = new (string, string?)[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var value = values.GetValue(i);
            result[i] = (value?.ToString() ?? "", GetDisplayName(value));
        }

        return result;
    }

    // Get the display text for an enum value:
    // - Use the DisplayAttribute if set on the enum member, so this support localization
    // - Fallback on Humanizer to decamelize the enum member name
    private static string? GetDisplayName(object? value)
    {
        if (value is null)
            return null;

        // Read the Display attribute name
        var valueAsString = value.ToString();
        if (valueAsString is not null)
        {
            var member = value.GetType().GetMember(valueAsString)[0];
            var displayAttribute = member.GetCustomAttribute<DisplayAttribute>();
            if (displayAttribute is not null)
                return displayAttribute.GetName();
        }

        return valueAsString;
    }

    // Get the actual enum type. It unwrap Nullable<T> if needed
    // MyEnum  => MyEnum
    // MyEnum? => MyEnum
    private static Type GetEnumType()
    {
        var nullableType = Nullable.GetUnderlyingType(typeof(TEnum));
        if (nullableType != null)
            return nullableType;

        return typeof(TEnum);
    }
}
