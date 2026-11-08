namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The value of a tri-state accessibility state: <c>aria-checked</c> (https://w3c.github.io/aria/#aria-checked) or <c>aria-pressed</c> (https://w3c.github.io/aria/#aria-pressed).</summary>
public enum AccessibilityCheckedState
{
    /// <summary>The element is not checked (or not pressed).</summary>
    False,

    /// <summary>The element is checked (or pressed).</summary>
    True,

    /// <summary>The element is partially checked (or partially pressed), e.g. an indeterminate checkbox.</summary>
    Mixed,
}
