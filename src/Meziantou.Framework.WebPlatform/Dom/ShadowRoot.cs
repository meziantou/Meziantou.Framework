namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://dom.spec.whatwg.org/#enumdef-shadowrootmode</summary>
/// <seealso href="https://dom.spec.whatwg.org/#enumdef-shadowrootmode"/>
[IdlInterface("ShadowRootMode", "https://dom.spec.whatwg.org/#enumdef-shadowrootmode")]
public enum ShadowRootMode
{
    /// <summary>The shadow root is accessible through the <c>shadowRoot</c> property of its host.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowrootmode-open"/>
    [IdlMember("open", "https://dom.spec.whatwg.org/#dom-shadowrootmode-open")]
    Open,

    /// <summary>The shadow root is not accessible through the <c>shadowRoot</c> property of its host.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowrootmode-closed"/>
    [IdlMember("closed", "https://dom.spec.whatwg.org/#dom-shadowrootmode-closed")]
    Closed,
}

/// <summary>https://dom.spec.whatwg.org/#enumdef-slotassignmentmode</summary>
/// <seealso href="https://dom.spec.whatwg.org/#enumdef-slotassignmentmode"/>
[IdlInterface("SlotAssignmentMode", "https://dom.spec.whatwg.org/#enumdef-slotassignmentmode")]
public enum SlotAssignmentMode
{
    /// <summary>Slottables are assigned to slots explicitly with <c>assign()</c>.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-slotassignmentmode-manual"/>
    [IdlMember("manual", "https://dom.spec.whatwg.org/#dom-slotassignmentmode-manual")]
    Manual,

    /// <summary>
    /// Slottables are assigned to slots by matching their <c>slot</c> attribute with the name of the slot.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-slotassignmentmode-named"/>
    [IdlMember("named", "https://dom.spec.whatwg.org/#dom-slotassignmentmode-named")]
    Named,
}

/// <summary>https://dom.spec.whatwg.org/#dictdef-shadowrootinit</summary>
/// <seealso href="https://dom.spec.whatwg.org/#dictdef-shadowrootinit"/>
[IdlInterface("ShadowRootInit", "https://dom.spec.whatwg.org/#dictdef-shadowrootinit")]
public sealed class ShadowRootInit
{
    /// <summary>Gets or sets the encapsulation mode of the shadow root.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowrootinit-mode"/>
    [IdlMember("mode", "https://dom.spec.whatwg.org/#dom-shadowrootinit-mode")]
    public required ShadowRootMode Mode { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the shadow root delegates focus to its first focusable element.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowrootinit-delegatesfocus"/>
    [IdlMember("delegatesFocus", "https://dom.spec.whatwg.org/#dom-shadowrootinit-delegatesfocus")]
    public bool DelegatesFocus { get; init; }

    /// <summary>Gets or sets how slottables are assigned to the slots of the shadow root.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowrootinit-slotassignment"/>
    [IdlMember("slotAssignment", "https://dom.spec.whatwg.org/#dom-shadowrootinit-slotassignment")]
    public SlotAssignmentMode SlotAssignment { get; init; } = SlotAssignmentMode.Named;

    /// <summary>Gets or sets a value indicating whether the shadow root is copied when its host is cloned.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowrootinit-clonable"/>
    [IdlMember("clonable", "https://dom.spec.whatwg.org/#dom-shadowrootinit-clonable")]
    public bool Clonable { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the shadow root is included when its host is serialized.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowrootinit-serializable"/>
    [IdlMember("serializable", "https://dom.spec.whatwg.org/#dom-shadowrootinit-serializable")]
    public bool Serializable { get; init; }

    /// <summary>Gets or sets the custom element registry of the shadow root.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowrootinit-customelementregistry"/>
    [IdlMember("customElementRegistry", "https://dom.spec.whatwg.org/#dom-shadowrootinit-customelementregistry")]
    public CustomElementRegistry? CustomElementRegistry
    {
        get;
        init
        {
            field = value;
            HasCustomElementRegistry = true;
        }
    }

    /// <summary>Whether the member exists (it may exist with a null value).</summary>
    internal bool HasCustomElementRegistry { get; private init; }
}

/// <summary>https://dom.spec.whatwg.org/#interface-shadowroot</summary>
/// <seealso href="https://dom.spec.whatwg.org/#shadowroot"/>
[IdlInterface("ShadowRoot", "https://dom.spec.whatwg.org/#shadowroot")]
public sealed partial class ShadowRoot : DocumentFragment
{
    internal ShadowRoot(Document document, Element host, ShadowRootMode mode)
        : base(document)
    {
        base.Host = host;
        Mode = mode;
    }

    /// <summary>Gets the encapsulation mode of the shadow root.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowroot-mode"/>
    [IdlMember("mode", "https://dom.spec.whatwg.org/#dom-shadowroot-mode")]
    public ShadowRootMode Mode { get; }

    /// <summary>
    /// Gets a value indicating whether the shadow root delegates focus to its first focusable element.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowroot-delegatesfocus"/>
    [IdlMember("delegatesFocus", "https://dom.spec.whatwg.org/#dom-shadowroot-delegatesfocus")]
    public bool DelegatesFocus { get; internal set; }

    /// <summary>Gets a value indicating whether the shadow root is included when its host is serialized.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowroot-serializable"/>
    [IdlMember("serializable", "https://dom.spec.whatwg.org/#dom-shadowroot-serializable")]
    public bool Serializable { get; internal set; }

    /// <summary>Gets how slottables are assigned to the slots of the shadow root.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowroot-slotassignment"/>
    [IdlMember("slotAssignment", "https://dom.spec.whatwg.org/#dom-shadowroot-slotassignment")]
    public SlotAssignmentMode SlotAssignment { get; internal set; }

    /// <summary>Gets a value indicating whether the shadow root is copied when its host is cloned.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowroot-clonable"/>
    [IdlMember("clonable", "https://dom.spec.whatwg.org/#dom-shadowroot-clonable")]
    public bool Clonable { get; internal set; }

    /// <summary>Gets the element to which the shadow root is attached.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-shadowroot-host"/>
    [IdlMember("host", "https://dom.spec.whatwg.org/#dom-shadowroot-host")]
    public new Element Host => base.Host!;

    /// <summary>https://dom.spec.whatwg.org/#shadowroot-declarative</summary>
    internal bool Declarative { get; set; }

    /// <summary>https://dom.spec.whatwg.org/#shadowroot-custom-element-registry</summary>
    internal CustomElementRegistry? CustomElementRegistryStorage { get; set; }

    /// <summary>https://dom.spec.whatwg.org/#shadowroot-keep-custom-element-registry-null</summary>
    internal bool KeepCustomElementRegistryNull { get; set; }

    /// <summary>https://dom.spec.whatwg.org/#dom-documentorshadowroot-customelementregistry</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-documentorshadowroot-customelementregistry"/>
    [IdlMember("customElementRegistry", "https://dom.spec.whatwg.org/#dom-documentorshadowroot-customelementregistry")]
    public CustomElementRegistry? CustomElementRegistry => CustomElementRegistryStorage;

    /// <summary>The IDs of the elements of the shadow tree, created on the first getElementById() call.</summary>
    internal IdMap? IdMap { get; set; }

    /// <summary>https://dom.spec.whatwg.org/#shadowroot-available-to-element-internals</summary>
    internal bool AvailableToElementInternals { get; set; }

    internal override Node CloneSingleNode(Document document) => throw DomException.NotSupported("Shadow roots cannot be cloned");

    /// <summary>https://dom.spec.whatwg.org/#get-the-parent (shadow root)</summary>
    internal override EventTarget? GetTheParent(Event @event)
    {
        if (!@event.ComposedFlag && @event.Path.Count > 0 && @event.Path[0].InvocationTarget is Node first && ReferenceEquals(first.GetRoot(), this))
            return null;

        return Host;
    }
}
