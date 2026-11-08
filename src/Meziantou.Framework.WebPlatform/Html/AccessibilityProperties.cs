namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The properties that the accessibility tree of a browser exposes for an element: its role, its accessible name and
/// description, and its states. The values are computed when the instance is created: they do not follow the later
/// changes of the document.
/// </summary>
/// <seealso cref="Element.GetAccessibilityProperties"/>
/// <seealso cref="AccessibilityNode"/>
public sealed class AccessibilityProperties
{
    internal AccessibilityProperties(
        string role,
        string name,
        string description,
        bool isHidden,
        AccessibilityCheckedState? isChecked,
        AccessibilityCheckedState? pressed,
        bool? expanded,
        bool? selected,
        bool disabled,
        int? level)
    {
        Role = role;
        Name = name;
        Description = description;
        IsHidden = isHidden;
        Checked = isChecked;
        Pressed = pressed;
        Expanded = expanded;
        Selected = selected;
        Disabled = disabled;
        Level = level;
    }

    internal static AccessibilityProperties ForText(string text) => new("text", text, "", isHidden: false, isChecked: null, pressed: null, expanded: null, selected: null, disabled: false, level: null);

    /// <summary>
    /// The role, with the values of <see cref="Element.ComputedRole"/> (e.g. <c>button</c>, <c>heading</c>, <c>generic</c>,
    /// <c>none</c>). The text of an <see cref="AccessibilityNode"/> has the role <c>text</c>.
    /// </summary>
    public string Role { get; }

    /// <summary>The accessible name (https://w3c.github.io/accname/), as <see cref="Element.ComputedName"/>. For a text node, the text.</summary>
    public string Name { get; }

    /// <summary>
    /// The accessible description (https://w3c.github.io/accname/#dfn-accessible-description): the text of the elements
    /// referenced by <c>aria-describedby</c>, else <c>aria-description</c>, else the <c>title</c> attribute when it is not the
    /// accessible name. The empty string when the element has no description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Whether the element is excluded from the accessibility tree: it is not rendered (<c>display: none</c>,
    /// <c>visibility: hidden</c>) or it is in a subtree with <c>aria-hidden="true"</c>.
    /// </summary>
    public bool IsHidden { get; }

    /// <summary>
    /// The checked state (https://w3c.github.io/aria/#aria-checked) of a checkbox, a radio button, a switch, or a checkable
    /// menu item, option or tree item: the native state of an <c>input</c> (an indeterminate checkbox is
    /// <see cref="AccessibilityCheckedState.Mixed"/>), else <c>aria-checked</c>. <see langword="null"/> when the role has no
    /// checked state.
    /// </summary>
    public AccessibilityCheckedState? Checked { get; }

    /// <summary>The pressed state of a toggle button (<c>aria-pressed</c>, https://w3c.github.io/aria/#aria-pressed). <see langword="null"/> when the element is not a toggle button.</summary>
    public AccessibilityCheckedState? Pressed { get; }

    /// <summary>The expanded state of a button, a link, a combobox, a tab, a row or a tree item (<c>aria-expanded</c>, https://w3c.github.io/aria/#aria-expanded). <see langword="null"/> when the element is not expandable.</summary>
    public bool? Expanded { get; }

    /// <summary>The selected state of an option, a tab, a row or a cell (<c>aria-selected</c>, or the selectedness of an <c>option</c>). <see langword="null"/> when the role has no selected state.</summary>
    public bool? Selected { get; }

    /// <summary>
    /// Whether the element is disabled: a disabled form control (including by a disabled <c>fieldset</c>, <c>optgroup</c> or
    /// <c>select</c>), or <c>aria-disabled="true"</c> on the element or one of its ancestors.
    /// </summary>
    public bool Disabled { get; }

    /// <summary>
    /// The hierarchical level (https://w3c.github.io/aria/#aria-level) of a heading, a list item, a row or a tree item:
    /// <c>aria-level</c>, else the computed heading level of an <c>h1</c>-<c>h6</c> element, which takes the
    /// <c>headingoffset</c> and <c>headingreset</c> attributes of its ancestors into account, else 2 for an element with
    /// <c>role="heading"</c>. <see langword="null"/> when the element has no level.
    /// </summary>
    public int? Level { get; }
}
