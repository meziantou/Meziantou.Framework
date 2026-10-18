using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    /// <summary>Returns a live collection of the descendant elements with the given qualified name.</summary>
    [IdlMember("getElementsByTagName", "https://dom.spec.whatwg.org/#dom-document-getelementsbytagname")]
    public HtmlCollection GetElementsByTagName(string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        return HtmlCollection.ByQualifiedName(this, qualifiedName);
    }

    /// <summary>Returns a live collection of the descendant elements with the given namespace and local name.</summary>
    [IdlMember("getElementsByTagNameNS", "https://dom.spec.whatwg.org/#dom-document-getelementsbytagnamens")]
    public HtmlCollection GetElementsByTagNameNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        return HtmlCollection.ByNamespaceAndLocalName(this, @namespace, localName);
    }

    /// <summary>Returns a live collection of the descendant elements that have all the given class names.</summary>
    [IdlMember("getElementsByClassName", "https://dom.spec.whatwg.org/#dom-document-getelementsbyclassname")]
    public HtmlCollection GetElementsByClassName(string classNames)
    {
        ArgumentNullException.ThrowIfNull(classNames);
        return HtmlCollection.ByClassNames(this, classNames);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-document-createevent</summary>
    [IdlMember("createEvent", "https://dom.spec.whatwg.org/#dom-document-createevent")]
    public Event CreateEvent(string @interface)
    {
        ArgumentNullException.ThrowIfNull(@interface);
        var name = InfraAscii.ToAsciiLowercase(@interface);
        var result = name switch
        {
            "beforeunloadevent" => BeforeUnloadEvent.CreateUninitializedBeforeUnloadEvent(),
            "compositionevent" => CompositionEvent.CreateUninitializedCompositionEvent(),
            "customevent" => CustomEvent.CreateUninitializedCustomEvent(),
            "dragevent" => DragEvent.CreateUninitializedDragEvent(),
            "event" or "events" or "htmlevents" or "svgevents" => Event.CreateUninitialized(),
            "focusevent" => FocusEvent.CreateUninitializedFocusEvent(),
            "hashchangeevent" => HashChangeEvent.CreateUninitializedHashChangeEvent(),
            "keyboardevent" => KeyboardEvent.CreateUninitializedKeyboardEvent(),
            "messageevent" => MessageEvent.CreateUninitializedMessageEvent(),
            "mouseevent" or "mouseevents" => MouseEvent.CreateUninitializedMouseEvent(),
            "storageevent" => StorageEvent.CreateUninitializedStorageEvent(),
            "textevent" => TextEvent.CreateUninitializedTextEvent(),
            "uievent" or "uievents" => UIEvent.CreateUninitializedUIEvent(),

            // Device motion/orientation and touch events are not supported, as there are no such devices
            _ => throw DomException.NotSupported($"The event interface '{@interface}' is not supported"),
        };

        return result;
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-document-createrange</summary>
    [IdlMember("createRange", "https://dom.spec.whatwg.org/#dom-document-createrange")]
    public Range CreateRange() => new(this);

    /// <summary>https://dom.spec.whatwg.org/#dom-document-createnodeiterator</summary>
    [IdlMember("createNodeIterator", "https://dom.spec.whatwg.org/#dom-document-createnodeiterator")]
    public NodeIterator CreateNodeIterator(Node root, uint whatToShow = NodeFilter.ShowAll, INodeFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new NodeIterator(root, whatToShow, filter);
    }

    /// <summary>https://dom.spec.whatwg.org/#dom-document-createtreewalker</summary>
    [IdlMember("createTreeWalker", "https://dom.spec.whatwg.org/#dom-document-createtreewalker")]
    public TreeWalker CreateTreeWalker(Node root, uint whatToShow = NodeFilter.ShowAll, INodeFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new TreeWalker(root, whatToShow, filter);
    }
}
