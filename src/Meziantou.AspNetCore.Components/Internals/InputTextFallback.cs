using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Components.Internals;

/// <summary>
/// A text input bound to an arbitrary <typeparamref name="TValue"/>, used by <see cref="GenericForm{TModel}"/> for
/// property types that have no dedicated editor. Unlike <see cref="InputText"/> (which is an
/// <see cref="InputBase{TValue}"/> of <see cref="string"/>) this stays generic, so the value, the change callback and
/// the value expression all agree on the property type.
/// </summary>
internal sealed class InputTextFallback<TValue> : InputBase<TValue>
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "type", "text");
        builder.AddAttribute(3, "class", CssClass);
        builder.AddAttribute(4, "value", BindConverter.FormatValue(CurrentValueAsString));
        builder.AddAttribute(5, "onchange", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString));
        builder.CloseElement();
    }

    protected override string? FormatValueAsString(TValue? value) => ValueConverter.ConvertToString(value);

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (ValueConverter.TryConvertFromString(value, typeof(TValue), out var converted))
        {
            result = (TValue)converted!;
            validationErrorMessage = null;
            return true;
        }

        result = default;
        validationErrorMessage = $"The {DisplayName ?? FieldIdentifier.FieldName} field is not valid.";
        return false;
    }
}
