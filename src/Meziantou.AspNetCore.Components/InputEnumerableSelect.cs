using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Formats.Asn1;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Components
{
    public sealed class InputEnumerableSelect<TClass> : InputBase<TClass>
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {

            string selectedValue = GetKeyValue(CurrentValue);
            builder.OpenElement(0, "select");
            builder.AddMultipleAttributes(1, AdditionalAttributes);
            builder.AddAttribute(2, "class", CssClass);
            builder.AddAttribute(3, "value", BindConverter.FormatValue(selectedValue));
            builder.AddAttribute(4, "onchange", EventCallback.Factory.CreateBinder<string?>(this, value => CurrentValueAsString = value, CurrentValueAsString, culture: null));

            foreach (var value in CurrentList)
            {
                string val = GetKeyValue(value);
                builder.OpenElement(5, "option");
                builder.AddAttribute(6, "value", val);
                builder.AddContent(8, GetDisplayName(value));
                builder.CloseElement();
            }
            builder.CloseElement(); // close the select element
        }

        private System.Collections.Generic.IEnumerable<TClass> currentList;
        private System.Collections.Generic.IEnumerable<TClass> CurrentList
        {
            get
            {
                if (currentList == null)
                    currentList = ((System.Collections.Generic.IEnumerable<TClass>)Activator.CreateInstance(typeof(TClass))).ToList();
                return currentList;
            }
        }

        protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TClass result, [NotNullWhen(false)] out string? validationErrorMessage)
        {
            result = CurrentList.Where(p => GetKeyValue(p) == value).FirstOrDefault();
            if (result != null)
            {
                validationErrorMessage = "";
                return true;
            }
            else
            {
                validationErrorMessage = $"The {FieldIdentifier.FieldName} field is not valid.";
                return false;
            }
        }

        private static string? GetDisplayName(TClass value)
        {
            if (value == null)
                return null;

            string valueAsString = $"Property 'Name' in {value.ToString()} not found";
            Type type = value.GetType();
            PropertyInfo prop = type.GetProperty("Name");

            if (prop != null)
                valueAsString = prop.GetValue(value, null).ToString();
            return valueAsString;
        }

        private static string GetKeyValue(TClass value)
        {
            PropertyInfo[] properties = value.GetType().GetProperties();
            foreach (PropertyInfo property in properties)
            {
                var attribute = Attribute.GetCustomAttribute(property, typeof(KeyAttribute)) as KeyAttribute;
                if (attribute != null) // This property has a KeyAttribute
                {
                    object val = property.GetValue(value);
                    return val.ToString();
                }
            }
            return $"KeyAttribute [Key] was not found in {value.ToString()}";
        }
    }
}
