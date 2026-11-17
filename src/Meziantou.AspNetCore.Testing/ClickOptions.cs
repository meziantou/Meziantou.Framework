using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing;

/// <summary>Configures a click.</summary>
public sealed class ClickOptions
{
    internal static ClickOptions Default { get; } = new();

    /// <summary>Gets the mouse button. The default is <see cref="MouseButton.Left"/>.</summary>
    public MouseButton Button { get; init; } = MouseButton.Left;

    /// <summary>Gets the number of clicks (2 for a double click). The default is 1.</summary>
    public int ClickCount { get; init; } = 1;

    /// <summary>Gets the modifier keys held during the click.</summary>
    public InputModifiers Modifiers { get; init; }

    /// <summary>Gets the position of the click in the border box of the element, or null for its center.</summary>
    public ElementPosition? Position { get; init; }

    /// <summary>
    /// Gets whether the click is sent without waiting until the element is actionable: visible, stable, enabled and not
    /// covered by another element. The element still needs a non-empty box.
    /// </summary>
    public bool Force { get; init; }

    /// <summary>Gets the maximum time to wait until the element is actionable, or null for <see cref="TestPage.DefaultTimeout"/>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets whether the click returns without waiting until the page is idle: only the tasks that the click queued run.
    /// Use it to check the state of the page while an asynchronous event handler is in progress (a loading indicator),
    /// then wait for the result with <see cref="TestPage.WaitForAssertionAsync"/>.
    /// </summary>
    public bool NoWaitAfter { get; init; }
}
