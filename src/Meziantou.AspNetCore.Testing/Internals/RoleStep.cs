using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

internal sealed class RoleStep(string role, TextMatcher? name, GetByRoleOptions options) : SearchStep
{
    protected override bool IsMatch(LocatorEvaluator evaluator, Element element)
    {
        if (!string.Equals(element.ComputedRole, role, StringComparison.Ordinal))
            return false;

        var properties = element.GetAccessibilityProperties();
        if (!options.IncludeHidden && properties.IsHidden)
            return false;

        if (name is not null && !name.IsMatch(GetName(evaluator, element, properties)))
            return false;

        if (options.Checked is { } isChecked && properties.Checked != (isChecked ? AccessibilityCheckedState.True : AccessibilityCheckedState.False))
            return false;

        if (options.Pressed is { } pressed && properties.Pressed != (pressed ? AccessibilityCheckedState.True : AccessibilityCheckedState.False))
            return false;

        if (options.Disabled is { } disabled && properties.Disabled != disabled)
            return false;

        if (options.Expanded is { } expanded && properties.Expanded != expanded)
            return false;

        if (options.Selected is { } selected && properties.Selected != selected)
            return false;

        if (options.Level is { } level && properties.Level != level)
            return false;

        return true;
    }

    /// <summary>
    /// Gets the accessible name. The accessible name of an element hidden from the accessibility tree is empty, so its name
    /// is its labels or its text, as the accessible name it would have if it were not hidden.
    /// </summary>
    private static string GetName(LocatorEvaluator evaluator, Element element, AccessibilityProperties properties)
    {
        if (!properties.IsHidden || properties.Name.Length > 0)
            return properties.Name;

        var labels = string.Join(' ', evaluator.GetLabels(element));
        return labels.Length > 0 ? labels : evaluator.GetText(element);
    }

    public override string ToString()
    {
        var builder = new StringBuilder("GetByRole(").Append(TextMatcher.Quote(role));
        if (name is not null)
        {
            builder.Append(", ").Append(name);
        }

        Append(builder, nameof(GetByRoleOptions.Checked), options.Checked);
        Append(builder, nameof(GetByRoleOptions.Disabled), options.Disabled);
        Append(builder, nameof(GetByRoleOptions.Expanded), options.Expanded);
        Append(builder, nameof(GetByRoleOptions.Pressed), options.Pressed);
        Append(builder, nameof(GetByRoleOptions.Selected), options.Selected);
        if (options.Level is { } level)
        {
            builder.Append(", Level = ").Append(TextMatcher.Format(level));
        }

        if (options.IncludeHidden)
        {
            builder.Append(", IncludeHidden = true");
        }

        return builder.Append(')').ToString();

        static void Append(StringBuilder builder, string name, bool? value)
        {
            if (value is { } actualValue)
            {
                builder.Append(", ").Append(name).Append(" = ").Append(actualValue ? "true" : "false");
            }
        }
    }
}
