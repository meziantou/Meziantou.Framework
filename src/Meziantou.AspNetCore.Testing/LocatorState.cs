namespace Meziantou.AspNetCore.Testing;

/// <summary>The state that <see cref="Locator.WaitForAsync"/> waits for.</summary>
public enum LocatorState
{
    /// <summary>The locator finds an element.</summary>
    Attached,

    /// <summary>The locator finds no element.</summary>
    Detached,

    /// <summary>The locator finds a visible element: a non-empty box, and not <c>visibility: hidden</c>.</summary>
    Visible,

    /// <summary>The locator finds no element, or a hidden element.</summary>
    Hidden,
}
