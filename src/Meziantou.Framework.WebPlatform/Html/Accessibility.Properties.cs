using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Html;

internal static partial class Accessibility
{
    // The roles that support a state (https://w3c.github.io/aria/#state_prop_def), as Playwright computes them for its aria snapshots
    private static readonly FrozenSet<string> CheckedRoles = FrozenSet.Create(StringComparer.Ordinal,
        "checkbox", "menuitemcheckbox", "menuitemradio", "option", "radio", "switch", "treeitem");

    // https://w3c.github.io/aria/#aria-checked: "mixed" is treated as "false" by the roles that do not support it
    private static readonly FrozenSet<string> MixedCheckedRoles = FrozenSet.Create(StringComparer.Ordinal, "checkbox", "menuitemcheckbox");

    private static readonly FrozenSet<string> ExpandedRoles = FrozenSet.Create(StringComparer.Ordinal,
        "application", "button", "checkbox", "columnheader", "combobox", "gridcell", "link", "listbox", "menuitem", "menuitemcheckbox",
        "menuitemradio", "row", "rowheader", "switch", "tab", "treeitem");

    private static readonly FrozenSet<string> SelectedRoles = FrozenSet.Create(StringComparer.Ordinal,
        "columnheader", "gridcell", "option", "row", "rowheader", "tab", "treeitem");

    private static readonly FrozenSet<string> DisabledRoles = FrozenSet.Create(StringComparer.Ordinal,
        "application", "button", "checkbox", "columnheader", "combobox", "grid", "gridcell", "group", "link", "listbox", "menu", "menubar",
        "menuitem", "menuitemcheckbox", "menuitemradio", "option", "radio", "radiogroup", "row", "rowheader", "scrollbar", "searchbox",
        "separator", "slider", "spinbutton", "switch", "tab", "tablist", "textbox", "toolbar", "tree", "treegrid", "treeitem");

    private static readonly FrozenSet<string> LevelRoles = FrozenSet.Create(StringComparer.Ordinal, "heading", "listitem", "row", "treeitem");

    /// <summary>Returns the accessibility properties of an element (<see cref="Element.GetAccessibilityProperties"/>).</summary>
    public static AccessibilityProperties GetProperties(Element element)
    {
        var cache = new ComputationCache(element);
        return GetProperties(element, GetComputedRole(element), cache, IsHidden(element));
    }

    private static AccessibilityProperties GetProperties(Element element, string role, ComputationCache cache, bool isHidden)
    {
        var name = GetName(element, cache);
        return new AccessibilityProperties(
            role,
            name,
            GetDescription(element, name, cache),
            isHidden,
            GetChecked(element, role),
            GetPressed(element, role),
            GetExpanded(element, role),
            GetSelected(element, role),
            IsDisabled(element, role),
            GetLevel(element, role));
    }

    /// <summary>
    /// The accessible description (https://w3c.github.io/accname/#mapping_additional_nd_description): aria-describedby, then
    /// aria-description, then the title attribute when it is not the name (https://w3c.github.io/html-aam/#accessible-description-computation).
    /// </summary>
    private static string GetDescription(Element element, string name, ComputationCache cache)
    {
        var describedBy = GetDescribedByElements(element);
        if (describedBy.Count > 0)
        {
            var computation = new NameComputation(element, cache);
            var parts = new List<string>();
            foreach (var target in describedBy)
            {
                var part = Flatten(computation.Compute(target, new Traversal(InLabelledBy: true, Recursion: true, InHiddenRoot: cache.IsHiddenInTree(target))));
                if (part.Length > 0)
                {
                    parts.Add(part);
                }
            }

            if (parts.Count > 0)
                return string.Join(' ', parts);
        }

        var description = element.GetAttributeValue("aria-description") ?? GetInternals(element)?.AriaDescription;
        if (!string.IsNullOrWhiteSpace(description))
            return Flatten(description);

        if (element.GetAttributeValue("title") is { } title && Flatten(title) is { Length: > 0 } flattenedTitle && flattenedTitle != name)
            return flattenedTitle;

        return "";
    }

    private static List<Element> GetDescribedByElements(Element element)
    {
        if (element.AriaDescribedByElements is { Count: > 0 } elements)
            return [.. elements];

        if (GetInternals(element)?.AriaDescribedByElements is { Count: > 0 } internalsElements && !element.HasAttribute("aria-describedby"))
            return [.. internalsElements];

        return [];
    }

    /// <summary>The value of an ARIA attribute, else of the default semantics of a custom element (ElementInternals), trimmed and lowercased.</summary>
    private static string? GetAriaToken(Element element, string attribute, Func<ElementInternals, string?> getDefault)
    {
        var value = element.GetAttributeValue(attribute) ?? (GetInternals(element) is { } internals ? getDefault(internals) : null);
        return value is null ? null : InfraAscii.ToAsciiLowercase(value.Trim());
    }

    private static AccessibilityCheckedState? GetChecked(Element element, string role)
    {
        if (!CheckedRoles.Contains(role))
            return null;

        if (element is HtmlInputElement { InputType: InputType.Checkbox or InputType.Radio } input)
        {
            if (input.InputType == InputType.Checkbox && input.Indeterminate)
                return AccessibilityCheckedState.Mixed;

            return input.Checked ? AccessibilityCheckedState.True : AccessibilityCheckedState.False;
        }

        return GetAriaToken(element, "aria-checked", internals => internals.AriaChecked) switch
        {
            "true" => AccessibilityCheckedState.True,
            "mixed" when MixedCheckedRoles.Contains(role) => AccessibilityCheckedState.Mixed,
            "false" or "mixed" => AccessibilityCheckedState.False,

            // An option or a tree item is checkable only when it has aria-checked
            _ => role is "option" or "treeitem" ? null : AccessibilityCheckedState.False,
        };
    }

    private static AccessibilityCheckedState? GetPressed(Element element, string role)
    {
        if (role != "button")
            return null;

        return GetAriaToken(element, "aria-pressed", internals => internals.AriaPressed) switch
        {
            "true" => AccessibilityCheckedState.True,
            "false" => AccessibilityCheckedState.False,
            "mixed" => AccessibilityCheckedState.Mixed,
            _ => null,
        };
    }

    private static bool? GetExpanded(Element element, string role)
    {
        if (!ExpandedRoles.Contains(role))
            return null;

        return GetAriaToken(element, "aria-expanded", internals => internals.AriaExpanded) switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };
    }

    private static bool? GetSelected(Element element, string role)
    {
        if (!SelectedRoles.Contains(role))
            return null;

        if (element is HtmlOptionElement option)
            return option.Selected;

        return GetAriaToken(element, "aria-selected", internals => internals.AriaSelected) == "true";
    }

    /// <summary>A disabled form control, or aria-disabled="true" on the element or its nearest ancestor of the flat tree with an aria-disabled value.</summary>
    private static bool IsDisabled(Element element, string role)
    {
        if (HtmlElementStates.IsActuallyDisabled(element))
            return true;

        if (!DisabledRoles.Contains(role))
            return false;

        for (Node? ancestor = element; ancestor is Element ancestorElement; ancestor = HtmlElement.GetFlatTreeParent(ancestor))
        {
            var value = ReferenceEquals(ancestorElement, element)
                ? GetAriaToken(element, "aria-disabled", internals => internals.AriaDisabled)
                : ancestorElement.GetAttributeValue("aria-disabled") is { } attribute ? InfraAscii.ToAsciiLowercase(attribute.Trim()) : null;

            switch (value)
            {
                case "true":
                    return true;
                case "false":
                    return false;
            }
        }

        return false;
    }

    private static int? GetLevel(Element element, string role)
    {
        if (!LevelRoles.Contains(role))
            return null;

        if (GetAriaToken(element, "aria-level", internals => internals.AriaLevel) is { } ariaLevel
            && int.TryParse(ariaLevel, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var level)
            && level >= 1)
        {
            return level;
        }

        if (role != "heading")
            return null;

        // The computed heading level takes headingoffset and headingreset into account (https://html.spec.whatwg.org/multipage/sections.html#get-an-element's-computed-heading-level)
        var headingLevel = HtmlElementStates.GetHeadingLevel(element);
        return headingLevel > 0 ? headingLevel : 2;
    }

    /// <summary>
    /// The state shared by the computations of a document: the relocations of aria-owns and the CSS counters, computed once
    /// for all the elements of an accessibility tree.
    /// </summary>
    private sealed class ComputationCache(Element element)
    {
        private AriaOwns? _owns;
        private CssCounters? _counters;

        public AriaOwns Owns => _owns ??= AriaOwns.Compute(element);

        public CssCounters Counters => _counters ??= CssCounters.Compute(element);

        /// <summary>The children of the element in the accessibility tree: its flat tree children, without the elements owned by another element, then the elements it owns (aria-owns).</summary>
        public AccessibleChildren GetChildren(Element parent) => new(parent, HtmlElement.GetFlatTreeChildren(parent), Owns.HasOwnedElements ? Owns : null);

        /// <summary>Whether the element is hidden, its parent being its owner (aria-owns) when it is owned: an owned element is not hidden by the aria-hidden of its DOM ancestors.</summary>
        public bool IsHiddenInTree(Element? target)
        {
            if (target is null)
                return false;

            if (!Owns.HasOwnedElements)
                return IsHidden(target);

            for (var ancestor = target; ancestor is not null; ancestor = Owns.GetOwner(ancestor) ?? HtmlElement.GetFlatTreeParent(ancestor) as Element)
            {
                if (IsAriaHidden(ancestor))
                    return true;
            }

            return IsNotRendered(target);
        }
    }
}
