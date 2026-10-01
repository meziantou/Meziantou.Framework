using System.Collections.Frozen;

namespace Meziantou.Framework.Language.Css.Internals;

/// <summary>The pseudo-classes and pseudo-elements a browser knows, as of the CSS specifications of 2026.</summary>
/// <remarks>
/// A selector naming one that is not here is still parsed, and reported as a warning: a browser drops the whole rule,
/// but the name may simply be newer than this list. Vendor-prefixed names are never reported.
/// </remarks>
internal static class KnownNames
{
    public static readonly FrozenSet<string> PseudoClasses = Set(
        "active", "active-view-transition", "any-link", "autofill", "blank", "buffering", "checked", "closed", "current", "default", "defined",
        "disabled", "empty", "enabled", "first", "first-child", "first-of-type", "focus", "focus-visible", "focus-within", "fullscreen",
        "future", "has-slotted", "host", "hover", "in-range", "indeterminate", "invalid", "last-child", "last-of-type", "left", "link",
        "local-link", "modal", "muted", "only-child", "only-of-type", "open", "optional", "out-of-range", "past", "paused",
        "picture-in-picture", "placeholder-shown", "playing", "popover-open", "read-only", "read-write", "required", "right", "root",
        "scope", "seeking", "stalled", "target", "target-current", "target-within", "user-invalid", "user-valid", "valid", "visited",
        "volume-locked", "xr-overlay");

    public static readonly FrozenSet<string> FunctionalPseudoClasses = Set(
        "active-view-transition-type", "current", "dir", "has", "heading", "host", "host-context", "is", "lang", "not", "nth-child",
        "nth-col", "nth-last-child", "nth-last-col", "nth-last-of-type", "nth-of-type", "state", "where");

    public static readonly FrozenSet<string> PseudoElements = Set(
        "after", "backdrop", "before", "checkmark", "column", "cue", "cue-region", "details-content", "file-selector-button", "first-letter",
        "first-line", "grammar-error", "marker", "picker-icon", "placeholder", "scroll-marker", "scroll-marker-group", "search-text",
        "selection", "spelling-error", "target-text", "view-transition");

    public static readonly FrozenSet<string> FunctionalPseudoElements = Set(
        "cue", "cue-region", "highlight", "part", "picker", "scroll-button", "slotted", "view-transition-group", "view-transition-image-pair",
        "view-transition-new", "view-transition-old");

    private static FrozenSet<string> Set(params string[] names) => names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
