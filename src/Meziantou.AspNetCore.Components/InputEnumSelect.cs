using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Components;

// Note that adding a constraint on TEnum (where T : Enum) doesn't work when used in the view, Razor raises an error at build time. Also, this would prevent using nullable types...
public sealed class InputEnumSelect<TEnum> : InputBase<TEnum>
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "select");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "class", CssClass);
        builder.AddAttribute(3, "value", BindConverter.FormatValue(CurrentValueAsString));
        builder.AddAttribute(4, "onchange", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString, culture: null));

        // Add an option element per enum value
        var enumType = InputEnumSelect<TEnum>.GetEnumType();
        foreach (TEnum value in Enum.GetValues(enumType))
        {
            builder.OpenElement(5, "option");
            builder.AddAttribute(6, "value", value.ToString());
            builder.AddContent(7, GetDisplayName(value));
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

    // Get the display text for an enum value:
    // - Use the DisplayAttribute if set on the enum member, so this support localization
    // - Fallback on Humanizer to decamelize the enum member name
    private static string? GetDisplayName(TEnum value)
    {
        if (value == null)
            return null;

        // Read the Display attribute name
        var valueAsString = value.ToString();
        if (valueAsString != null)
        {
            var member = value.GetType().GetMember(valueAsString)[0];
            var displayAttribute = member.GetCustomAttribute<DisplayAttribute>();
            if (displayAttribute != null)
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
