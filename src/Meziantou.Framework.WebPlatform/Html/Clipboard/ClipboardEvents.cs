using System.Numerics;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://w3c.github.io/clipboard-apis/#dictdef-clipboardeventinit</summary>
[IdlInterface("ClipboardEventInit", "https://w3c.github.io/clipboard-apis/#dictdef-clipboardeventinit")]
public sealed class ClipboardEventInit : EventInit
{
    /// <summary>Gets the data transfer object that carries the clipboard data of the event.</summary>
    [IdlMember("clipboardData", "https://w3c.github.io/clipboard-apis/#dom-clipboardeventinit-clipboarddata")]
    public DataTransfer? ClipboardData { get; init; }
}

/// <summary>
/// The copy, cut and paste events (https://w3c.github.io/clipboard-apis/#clipboard-event-interfaces). The events fired by
/// the clipboard actions are trusted and composed, and their <see cref="ClipboardData"/> is only associated with the
/// clipboard while they are dispatched.
/// </summary>
[IdlInterface("ClipboardEvent", "https://w3c.github.io/clipboard-apis/#clipboardevent")]
public sealed class ClipboardEvent : Event
{
    /// <summary>Creates a clipboard event of the given type, initialized from the optional dictionary.</summary>
    [IdlMember("constructor", "https://w3c.github.io/clipboard-apis/#dom-clipboardevent-clipboardevent")]
    public ClipboardEvent(string type, ClipboardEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        ClipboardData = eventInitDict?.ClipboardData;
    }

    /// <summary>https://w3c.github.io/clipboard-apis/#dom-clipboardevent-clipboarddata</summary>
    [IdlMember("clipboardData", "https://w3c.github.io/clipboard-apis/#dom-clipboardevent-clipboarddata")]
    public DataTransfer? ClipboardData { get; }
}

/// <summary>https://w3c.github.io/clipboard-apis/#dictdef-clipboardchangeeventinit</summary>
[IdlInterface("ClipboardChangeEventInit", "https://w3c.github.io/clipboard-apis/#dictdef-clipboardchangeeventinit")]
public sealed class ClipboardChangeEventInit : EventInit
{
    /// <summary>Gets the data types available on the system clipboard.</summary>
    [IdlMember("types", "https://w3c.github.io/clipboard-apis/#dom-clipboardchangeeventinit-types")]
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>Gets the identifier of the change of the clipboard.</summary>
    [IdlMember("changeId", "https://w3c.github.io/clipboard-apis/#dom-clipboardchangeeventinit-changeid")]
    public BigInteger ChangeId { get; init; }
}

/// <summary>The clipboardchange event (https://w3c.github.io/clipboard-apis/#clipboardchangeevent), fired at <c>navigator.clipboard</c> when the system clipboard changes.</summary>
[IdlInterface("ClipboardChangeEvent", "https://w3c.github.io/clipboard-apis/#clipboardchangeevent")]
public sealed class ClipboardChangeEvent : Event
{
    /// <summary>Creates a clipboard change event of the given type, initialized from the optional dictionary.</summary>
    [IdlMember("constructor", "https://w3c.github.io/clipboard-apis/#dom-clipboardchangeevent-clipboardchangeevent")]
    public ClipboardChangeEvent(string type, ClipboardChangeEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        // https://w3c.github.io/clipboard-apis/#clipboardchangeevent-constructor-steps: a clone of the types
        Types = (eventInitDict?.Types ?? []).ToArray();
        ChangeId = eventInitDict?.ChangeId ?? BigInteger.Zero;
    }

    /// <summary>Gets the mandatory data types available on the system clipboard (https://w3c.github.io/clipboard-apis/#dom-clipboardchangeevent-types).</summary>
    [IdlMember("types", "https://w3c.github.io/clipboard-apis/#dom-clipboardchangeevent-types")]
    public IReadOnlyList<string> Types { get; }

    /// <summary>Gets the identifier of the change of the clipboard (https://w3c.github.io/clipboard-apis/#dom-clipboardchangeevent-changeid).</summary>
    [IdlMember("changeId", "https://w3c.github.io/clipboard-apis/#dom-clipboardchangeevent-changeid")]
    public BigInteger ChangeId { get; }
}
