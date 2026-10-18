namespace Meziantou.Framework.WebPlatform.Dom;

// https://w3c.github.io/pointerevents/#extensions-to-the-element-interface: the pointer capture of the pointers of the user
// (see UserInput)
public partial class Element
{
    /// <summary>
    /// Sets the pointer capture of an active pointer to the element (https://w3c.github.io/pointerevents/#dom-element-setpointercapture):
    /// its subsequent events are fired at the element until the capture is released. The pointer must be in the active buttons
    /// state.
    /// </summary>
    [IdlMember("setPointerCapture", "https://w3c.github.io/pointerevents/#dom-element-setpointercapture")]
    public void SetPointerCapture(int pointerId) => UserInput.SetPointerCapture(this, pointerId);

    /// <summary>Releases the pointer capture of a pointer when the element captures it (https://w3c.github.io/pointerevents/#dom-element-releasepointercapture).</summary>
    [IdlMember("releasePointerCapture", "https://w3c.github.io/pointerevents/#dom-element-releasepointercapture")]
    public void ReleasePointerCapture(int pointerId) => UserInput.ReleasePointerCapture(this, pointerId);

    /// <summary>Returns whether the element captures (or is about to capture) a pointer (https://w3c.github.io/pointerevents/#dom-element-haspointercapture).</summary>
    [IdlMember("hasPointerCapture", "https://w3c.github.io/pointerevents/#dom-element-haspointercapture")]
    public bool HasPointerCapture(int pointerId) => UserInput.HasPointerCapture(this, pointerId);
}
