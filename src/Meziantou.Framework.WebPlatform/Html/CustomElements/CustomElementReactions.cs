using System.Diagnostics;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>The lifecycle callbacks of a custom element definition (https://html.spec.whatwg.org/multipage/custom-elements.html#concept-custom-element-definition-lifecycle-callbacks).</summary>
internal enum CustomElementCallbackKind : byte
{
    Connected,
    Disconnected,
    ConnectedMove,
    Adopted,
    AttributeChanged,
    FormAssociated,
    FormReset,
    FormDisabled,
    FormStateRestore,
}

/// <summary>An item of the custom element reaction queue of an element: an upgrade reaction or a callback reaction.</summary>
internal readonly struct CustomElementReaction
{
    private CustomElementReaction(CustomElementDefinition definition, CustomElementCallbackKind kind, object? callback, object?[]? arguments, bool isUpgrade)
    {
        Definition = definition;
        Kind = kind;
        Callback = callback;
        Arguments = arguments;
        IsUpgrade = isUpgrade;
    }

    public CustomElementDefinition Definition { get; }

    public bool IsUpgrade { get; }

    public CustomElementCallbackKind Kind { get; }

    /// <summary>The callback of the definition, or <see cref="CustomElementDefinition.MoveFallback"/> (disconnectedCallback then connectedCallback).</summary>
    public object? Callback { get; }

    public object?[]? Arguments { get; }

    public static CustomElementReaction Upgrade(CustomElementDefinition definition) => new(definition, default, callback: null, arguments: null, isUpgrade: true);

    public static CustomElementReaction CallbackReaction(CustomElementDefinition definition, CustomElementCallbackKind kind, object callback, object?[] arguments) => new(definition, kind, callback, arguments, isUpgrade: false);
}

/// <summary>
/// The custom element reactions stack of the similar-origin window agent (https://html.spec.whatwg.org/multipage/custom-elements.html#custom-element-reactions-stack).
/// Each thread has its own stack: the reactions are invoked synchronously when the operations that enqueued them end, so
/// the agents that run on a thread can share a stack.
/// </summary>
/// <remarks>
/// The [CEReactions] extended attribute pushes an element queue when an IDL member is called by author code. .NET code
/// calls the members directly, including from the implementation of other members, so the library tracks the nesting of
/// the operations since the last call to author code (<see cref="Depth"/>): an operation pushes an element queue only
/// when it is not nested in another operation. The calls to author code (custom element constructors and callbacks, event
/// listeners...) reset the depth, so the operations called by author code push their own element queue, as specified.
/// </remarks>
internal sealed class CustomElementReactionsStack
{
    [ThreadStatic]
    private static CustomElementReactionsStack? s_current;

    private readonly List<Queue<Element>> _stack = [];
    private readonly Stack<Queue<Element>> _pool = new();

    /// <summary>Gets the custom element reactions stack of the current thread.</summary>
    public static CustomElementReactionsStack Current => s_current ??= new CustomElementReactionsStack();

    /// <summary>The number of nested operations since the last call to author code.</summary>
    public int Depth { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#active-custom-element-constructor-map</summary>
    public Dictionary<CustomElementConstructor, CustomElementRegistry?> ActiveConstructorMap { get; } = new(ReferenceEqualityComparer.Instance);

    public bool IsEmpty => _stack.Count == 0;

    /// <summary>Pushes a new element queue.</summary>
    public void Push()
    {
        _stack.Add(_pool.Count > 0 ? _pool.Pop() : new Queue<Element>());
    }

    /// <summary>Pops the current element queue and invokes its reactions.</summary>
    public void PopAndInvoke()
    {
        var queue = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        try
        {
            CustomElementReactions.InvokeReactions(queue);
        }
        finally
        {
            queue.Clear();
            _pool.Push(queue);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#enqueue-an-element-on-the-appropriate-element-queue</summary>
    public void EnqueueElement(Element element)
    {
        if (_stack.Count > 0)
        {
            _stack[^1].Enqueue(element);
            return;
        }

        // The backup element queue of the agent is processed by a microtask. Without an event loop (a document that is not
        // associated with a browsing context), the reactions stay in the reaction queue of the element: they are invoked
        // with the next reactions of the element that are enqueued in an element queue.
        element.NodeDocument.EventLoop?.EnqueueOnBackupElementQueue(element);
    }
}

/// <summary>
/// Implements [CEReactions] (https://html.spec.whatwg.org/multipage/custom-elements.html#cereactions) for an operation of the
/// library: an element queue is pushed when the operation is not nested in another operation, and its reactions are invoked
/// when the scope is disposed.
/// </summary>
internal readonly ref struct CEReactionsScope
{
    private readonly CustomElementReactionsStack? _stack;
    private readonly bool _pushed;

    private CEReactionsScope(CustomElementReactionsStack stack, bool pushed)
    {
        _stack = stack;
        _pushed = pushed;
    }

    /// <summary>Whether the scope entered the operation (false when no custom element was defined).</summary>
    public bool IsActive => _stack is not null;

    /// <summary>Enters an operation decorated with [CEReactions].</summary>
    public static CEReactionsScope Enter()
    {
        // Nothing can enqueue reactions until an element is defined: skip the thread-static lookup
        if (!CustomElementRegistry.HasDefinitions)
            return default;

        var stack = CustomElementReactionsStack.Current;
        var push = stack.Depth == 0;
        stack.Depth++;
        if (push)
        {
            stack.Push();
        }

        return new CEReactionsScope(stack, push);
    }

    /// <summary>Enters an operation that pushes an element queue in any case (e.g. the HTML parser when it creates a custom element).</summary>
    public static CEReactionsScope EnterAndPush()
    {
        var stack = CustomElementReactionsStack.Current;
        stack.Depth++;
        stack.Push();
        return new CEReactionsScope(stack, pushed: true);
    }

    /// <summary>Enters an operation that is not decorated with [CEReactions]: the reactions go to the current element queue, or to the backup element queue.</summary>
    public static CEReactionsScope EnterWithoutPush()
    {
        if (!CustomElementRegistry.HasDefinitions)
            return default;

        var stack = CustomElementReactionsStack.Current;
        stack.Depth++;
        return new CEReactionsScope(stack, pushed: false);
    }

    public void Dispose()
    {
        if (_stack is null)
            return;

        _stack.Depth--;
        if (_pushed)
        {
            _stack.PopAndInvoke();
        }
    }
}

/// <summary>Marks a call to author code: the operations it calls push their own element queue.</summary>
internal readonly ref struct AuthorCodeScope
{
    private readonly CustomElementReactionsStack? _stack;
    private readonly int _depth;

    private AuthorCodeScope(CustomElementReactionsStack stack)
    {
        _stack = stack;
        _depth = stack.Depth;
        stack.Depth = 0;
    }

    public static AuthorCodeScope Enter()
    {
        if (!CustomElementRegistry.HasDefinitions)
            return default;

        return new AuthorCodeScope(CustomElementReactionsStack.Current);
    }

    public void Dispose()
    {
        if (_stack is not null)
        {
            _stack.Depth = _depth;
        }
    }
}

/// <summary>The custom element reactions (https://html.spec.whatwg.org/multipage/custom-elements.html#custom-element-reactions) and upgrades.</summary>
internal static class CustomElementReactions
{
    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#enqueue-a-custom-element-callback-reaction</summary>
    public static void EnqueueCallbackReaction(Element element, CustomElementCallbackKind kind, object?[] arguments)
    {
        var definition = element.CustomElementDefinition;
        Debug.Assert(definition is not null);
        var callback = definition.GetCallback(kind);
        if (kind == CustomElementCallbackKind.ConnectedMove && callback is null)
        {
            if (definition.GetCallback(CustomElementCallbackKind.Connected) is null && definition.GetCallback(CustomElementCallbackKind.Disconnected) is null)
                return;

            callback = CustomElementDefinition.MoveFallback;
        }

        if (callback is null)
            return;

        if (kind == CustomElementCallbackKind.AttributeChanged && !definition.ObservesAttribute((string)arguments[0]!))
            return;

        element.ElementRareData.CustomElementReactionQueue.Enqueue(CustomElementReaction.CallbackReaction(definition, kind, callback, arguments));
        CustomElementReactionsStack.Current.EnqueueElement(element);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#enqueue-a-custom-element-upgrade-reaction</summary>
    public static void EnqueueUpgradeReaction(Element element, CustomElementDefinition definition)
    {
        element.ElementRareData.CustomElementReactionQueue.Enqueue(CustomElementReaction.Upgrade(definition));
        CustomElementReactionsStack.Current.EnqueueElement(element);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#invoke-custom-element-reactions</summary>
    public static void InvokeReactions(Queue<Element> queue)
    {
        while (queue.TryDequeue(out var element))
        {
            if (element.RareDataOrNull is not ElementRareData { CustomElementReactionQueueOrNull: { } reactions })
                continue;

            while (reactions.TryDequeue(out var reaction))
            {
                if (reaction.IsUpgrade)
                {
                    try
                    {
                        Upgrade(element, reaction.Definition);
                    }
#pragma warning disable CA1031 // "report the exception"
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        reaction.Definition.Constructor.ReportException(ex, element.NodeDocument);
                    }
                }
                else
                {
                    InvokeCallback(element, reaction);
                }
            }
        }
    }

    private static void InvokeCallback(Element element, CustomElementReaction reaction)
    {
        var definition = reaction.Definition;
        if (ReferenceEquals(reaction.Callback, CustomElementDefinition.MoveFallback))
        {
            if (definition.GetCallback(CustomElementCallbackKind.Disconnected) is { } disconnected)
            {
                definition.Constructor.InvokeCallbackAndReport(element, CustomElementCallbackKind.Disconnected, disconnected, []);
            }

            if (definition.GetCallback(CustomElementCallbackKind.Connected) is { } connected)
            {
                definition.Constructor.InvokeCallbackAndReport(element, CustomElementCallbackKind.Connected, connected, []);
            }

            return;
        }

        definition.Constructor.InvokeCallbackAndReport(element, reaction.Kind, reaction.Callback!, reaction.Arguments!);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#concept-upgrade-an-element</summary>
    public static void Upgrade(Element element, CustomElementDefinition definition)
    {
        if (element.CustomElementState is not (CustomElementState.Undefined or CustomElementState.Uncustomized))
            return;

        element.CustomElementDefinition = definition;
        element.CustomElementState = CustomElementState.Failed;
        for (var i = 0; i < element.AttributeCount; i++)
        {
            EnqueueCallbackReaction(element, CustomElementCallbackKind.AttributeChanged, [element.GetAttributeLocalName(i), null, element.GetAttributeValue(i), element.GetAttributeNamespace(i)]);
        }

        if (element.IsConnected)
        {
            EnqueueCallbackReaction(element, CustomElementCallbackKind.Connected, []);
        }

        definition.ConstructionStack.Add(element);
        var constructor = definition.Constructor;
        var activeConstructorMap = CustomElementReactionsStack.Current.ActiveConstructorMap;
        var hasPreviousRegistry = activeConstructorMap.TryGetValue(constructor, out var previousRegistry);
        activeConstructorMap[constructor] = element.CustomElementRegistry;
        try
        {
            if (definition.DisableShadow && element.AttachedShadowRoot is not null)
                throw DomException.NotSupported("The custom element definition disables the shadow root, but the element is a shadow host");

            element.CustomElementState = CustomElementState.Precustomized;
            HtmlElement constructResult;
            using (AuthorCodeScope.Enter())
            {
                constructResult = constructor.Construct(definition, element.NodeDocument, element.CustomElementRegistry);
            }

            if (!ReferenceEquals(constructResult, element))
                throw new TypeErrorException("The custom element constructor did not return the upgraded element");
        }
        catch
        {
            element.CustomElementDefinition = null;
            element.ElementRareData.CustomElementReactionQueueOrNull?.Clear();
            throw;
        }
        finally
        {
            if (hasPreviousRegistry)
            {
                activeConstructorMap[constructor] = previousRegistry;
            }
            else
            {
                activeConstructorMap.Remove(constructor);
            }

            definition.ConstructionStack.RemoveAt(definition.ConstructionStack.Count - 1);
        }

        // The element is a form-associated custom element once it is custom (step 12 is done before step 11)
        element.CustomElementState = CustomElementState.Custom;
        if (element is HtmlElement { IsFormAssociatedCustomElement: true } formAssociated)
        {
            formAssociated.OnFormAssociatedCustomElementUpgraded();
        }

        // The element may now be in other categories (e.g. listed elements) and match other selectors (:defined)
        element.NodeDocument.IncrementDomVersion();
        element.NodeDocument.IncrementElementStateVersion();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#concept-try-upgrade</summary>
    public static void TryUpgrade(Element element)
    {
        var definition = CustomElementRegistry.LookUpDefinition(element.CustomElementRegistry, element.NamespaceUri, element.LocalName, element.IsValue);
        if (definition is not null)
        {
            EnqueueUpgradeReaction(element, definition);
        }
    }
}
