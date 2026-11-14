using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Testing.Components.Navigation;

/// <summary>A navigation requested by a component or by a click on a link.</summary>
/// <param name="Uri">The absolute URI.</param>
/// <param name="Options">The options of the navigation.</param>
/// <param name="IsInterceptedLink">Whether the navigation is a click on a link intercepted by the router.</param>
public sealed record NavigationHistoryEntry(string Uri, NavigationOptions Options, bool IsInterceptedLink);
