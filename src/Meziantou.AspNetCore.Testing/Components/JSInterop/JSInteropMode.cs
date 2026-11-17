namespace Meziantou.AspNetCore.Testing.Components.JSInterop;

/// <summary>What a JavaScript call that is not set up returns when the page does not run JavaScript.</summary>
public enum JSInteropMode
{
    /// <summary>The call returns the default value of its result type.</summary>
    Loose,

    /// <summary>The call throws a <see cref="UnplannedJSInvocationException"/>.</summary>
    Strict,
}
