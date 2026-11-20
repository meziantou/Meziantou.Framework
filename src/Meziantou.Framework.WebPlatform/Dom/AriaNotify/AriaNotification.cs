using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The priorities of a notification of <c>ariaNotify()</c> (https://w3c.github.io/aria/#enumdef-arianotifypriority).</summary>
/// <seealso href="https://w3c.github.io/aria/#enumdef-arianotifypriority"/>
[IdlInterface("AriaNotifyPriority", "https://w3c.github.io/aria/#enumdef-arianotifypriority")]
public enum AriaNotifyPriority
{
    /// <summary>The notification is announced after the current speech of the assistive technology.</summary>
    /// <seealso href="https://w3c.github.io/aria/#dom-arianotifypriority-normal"/>
    [IdlMember("normal", "https://w3c.github.io/aria/#dom-arianotifypriority-normal")]
    Normal,

    /// <summary>The notification may interrupt the current speech of the assistive technology.</summary>
    /// <seealso href="https://w3c.github.io/aria/#dom-arianotifypriority-high"/>
    [IdlMember("high", "https://w3c.github.io/aria/#dom-arianotifypriority-high")]
    High,
}

/// <summary>The options of <c>ariaNotify()</c> (https://w3c.github.io/aria/#dom-arianotificationoptions).</summary>
/// <seealso href="https://w3c.github.io/aria/#dom-arianotificationoptions"/>
[IdlInterface("AriaNotificationOptions", "https://w3c.github.io/aria/#dom-arianotificationoptions")]
public sealed class AriaNotificationOptions
{
    /// <summary>Gets the priority of the notification. The default is <see cref="AriaNotifyPriority.Normal"/>.</summary>
    /// <seealso href="https://w3c.github.io/aria/#dom-arianotificationoptions-priority"/>
    [IdlMember("priority", "https://w3c.github.io/aria/#dom-arianotificationoptions-priority")]
    public AriaNotifyPriority Priority { get; init; }
}

/// <summary>
/// A notification that a page sends to the assistive technologies with <c>ariaNotify()</c>
/// (https://w3c.github.io/aria/#ARIANotifyMixin): see <see cref="BrowsingContextOptions.AriaNotification"/>.
/// </summary>
public sealed class AriaNotification
{
    internal AriaNotification(Node source, string announcement, AriaNotifyPriority priority)
    {
        Source = source;
        Announcement = announcement;
        Priority = priority;
    }

    /// <summary>
    /// Gets the node whose <c>ariaNotify()</c> method was called: an <see cref="Element"/> or a <see cref="Document"/>. The
    /// notification inherits the language and the direction of the element.
    /// </summary>
    public Node Source { get; }

    /// <summary>Gets the text to announce.</summary>
    public string Announcement { get; }

    /// <summary>Gets the priority of the notification.</summary>
    public AriaNotifyPriority Priority { get; }

    /// <summary>
    /// The processing steps of <c>ariaNotify()</c> (https://w3c.github.io/aria/#dom-arianotifymixin-arianotify): the
    /// notification is sent when the document is allowed to use the <c>aria-notify</c> feature and the node is not excluded
    /// from the accessibility tree (a hidden element).
    /// </summary>
    internal static void Notify(Node node, string announcement, AriaNotificationOptions? options)
    {
        var document = node as Document ?? node.NodeDocument;
        if (!document.IsFullyActive || document.RelevantWindow is not { } window || window.Options.AriaNotification is not { } callback)
            return;

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.AriaNotify))
            return;

        if (node is Element element && (!element.IsConnected || Accessibility.IsHidden(element)))
            return;

        callback(CallbackContext.For(window), new AriaNotification(node, announcement, options?.Priority ?? AriaNotifyPriority.Normal));
    }
}

public partial class Element
{
    /// <summary>
    /// Sends a notification to the assistive technologies (https://w3c.github.io/aria/#ARIANotifyMixin): the host receives it
    /// with <see cref="BrowsingContextOptions.AriaNotification"/>. Nothing is sent when the element is excluded from the
    /// accessibility tree (e.g. <c>display: none</c> or <c>aria-hidden="true"</c>), or when the document is not allowed to
    /// use the <c>aria-notify</c> feature.
    /// </summary>
    /// <seealso href="https://w3c.github.io/aria/#dom-arianotifymixin-arianotify"/>
    [IdlMember("ariaNotify", "https://w3c.github.io/aria/#dom-arianotifymixin-arianotify")]
    public void AriaNotify(string announcement, AriaNotificationOptions? options = null) => AriaNotification.Notify(this, announcement, options);
}

public partial class Document
{
    /// <summary>
    /// Sends a notification to the assistive technologies (https://w3c.github.io/aria/#ARIANotifyMixin): the host receives it
    /// with <see cref="BrowsingContextOptions.AriaNotification"/>. Nothing is sent when the document is not allowed to use the
    /// <c>aria-notify</c> feature.
    /// </summary>
    /// <seealso href="https://w3c.github.io/aria/#dom-arianotifymixin-arianotify"/>
    [IdlMember("ariaNotify", "https://w3c.github.io/aria/#dom-arianotifymixin-arianotify")]
    public void AriaNotify(string announcement, AriaNotificationOptions? options = null) => AriaNotification.Notify(this, announcement, options);
}
