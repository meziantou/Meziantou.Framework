using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Components;

/// <summary>An input component for editing URL values.</summary>
/// <typeparam name="TValue">The type of the value. Supported types are <see cref="string"/> and <see cref="Uri"/>.</typeparam>
public class InputUrl<TValue> : InputBase<TValue>
{
    /// <summary>Gets or sets the error message to display when the input value cannot be parsed.</summary>
    [Parameter] public string ParsingErrorMessage { get; set; } = "";

    /// <summary>Gets a reference to the rendered input element.</summary>
    [DisallowNull] public ElementReference? Element { get; protected set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "type", "url");
        builder.AddAttribute(3, "class", CssClass);
        builder.AddAttribute(4, "value", BindConverter.FormatValue(CurrentValueAsString));
        builder.AddAttribute(5, "onchange", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.AddElementReferenceCapture(6, inputReference => Element = inputReference);
        builder.CloseElement();
    }

    /// <inheritdoc />
    protected override string FormatValueAsString(TValue? value)
    {
        return value switch
        {
            string str => str,
            Uri uri => uri.ToString(),
            _ => "",// Handles null for Nullable<DateTime>, etc.
        };
    }

    /// <inheritdoc />
    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        // Unwrap nullable types. We don't have to deal with receiving empty values for nullable
        // types here, because the underlying InputBase already covers that.
        var targetType = typeof(TValue);

        bool success;
        if (targetType == typeof(string))
        {
            success = TryParseString(value, out result);
        }
        else if (targetType == typeof(Uri))
        {
            success = TryParseUri(value, out result);
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

    private static bool TryParseString(string? value, out TValue? result)
    {
        if (value is not null)
        {
            result = (TValue)(object)value;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }

    private static bool TryParseUri(string? value, out TValue? result)
    {
        if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
        {
            result = (TValue)(object)uri;
            return true;
        }
        else
        {
            result = default;
            return false;
        }
    }
}
