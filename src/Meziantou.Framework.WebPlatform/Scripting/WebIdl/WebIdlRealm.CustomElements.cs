using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A JavaScript custom element constructor (https://html.spec.whatwg.org/multipage/custom-elements.html#custom-element-constructor):
/// the constructor of a class that extends HTMLElement. Constructing it calls the HTML element constructor (super()), which
/// creates the element or returns the element being upgraded.
/// </summary>
internal sealed class JsCustomElementConstructor<TAdapter, TValue>(WebIdlRealm<TAdapter, TValue> realm, TValue function) : CustomElementConstructor
    where TAdapter : struct, IJsAdapter<TValue>
    where TValue : class
{
    private static readonly string[] LifecycleCallbackNames = ["connectedCallback", "disconnectedCallback", "connectedMoveCallback", "adoptedCallback", "attributeChangedCallback"];
    private static readonly string[] FormCallbackNames = ["formAssociatedCallback", "formResetCallback", "formDisabledCallback", "formStateRestoreCallback"];

    public TValue Function { get; } = function;

    /// <summary>The realm of the operation that runs the constructor (e.g. define(), whose errors are created in the realm of the registry).</summary>
    private WebIdlRealm<TAdapter, TValue> ErrorRealm => Document.CurrentGlobalWindow is { } window ? realm.Agent.GetRealm(window) : realm;

    internal override bool IsConstructor => realm.Adapter.IsConstructor(Function);

    /// <summary>https://html.spec.whatwg.org/multipage/custom-elements.html#dom-customelementregistry-define (step 14)</summary>
    internal override CustomElementDefinitionData GetDefinitionData()
    {
        var adapter = realm.Adapter;
        var data = new CustomElementDefinitionData();
        var prototype = adapter.Get(Function, realm.Key("prototype"));
        if (!adapter.IsObject(prototype))
            throw ErrorRealm.CreateTypeError("The prototype of the custom element constructor is not an object");

        for (var i = 0; i < LifecycleCallbackNames.Length; i++)
        {
            data.LifecycleCallbacks[i] = GetCallback(prototype, LifecycleCallbackNames[i]);
        }

        if (data.LifecycleCallbacks[(int)CustomElementCallbackKind.AttributeChanged] is not null)
        {
            var observedAttributes = adapter.Get(Function, realm.Key("observedAttributes"));
            if (!adapter.IsUndefined(observedAttributes))
            {
                data.ObservedAttributes = ToStringSequence(observedAttributes);
            }
        }

        var disabledFeatures = adapter.Get(Function, realm.Key("disabledFeatures"));
        if (!adapter.IsUndefined(disabledFeatures))
        {
            var features = ToStringSequence(disabledFeatures);
            data.DisableInternals = features.Contains("internals");
            data.DisableShadow = features.Contains("shadow");
        }

        data.FormAssociated = adapter.ToBoolean(adapter.Get(Function, realm.Key("formAssociated")));
        if (data.FormAssociated)
        {
            for (var i = 0; i < FormCallbackNames.Length; i++)
            {
                data.LifecycleCallbacks[(int)CustomElementCallbackKind.FormAssociated + i] = GetCallback(prototype, FormCallbackNames[i]);
            }
        }

        return data;
    }

    private TValue? GetCallback(TValue prototype, string name)
    {
        var value = realm.Adapter.Get(prototype, realm.Key(name));
        if (realm.Adapter.IsUndefined(value))
            return null;

        // https://webidl.spec.whatwg.org/#es-callback-function
        if (!realm.Adapter.IsCallable(value))
            throw ErrorRealm.CreateTypeError($"'{name}' is not a function");

        return value;
    }

    private List<string> ToStringSequence(TValue value) => realm.ToSequence(value, static (r, v) => r.ToDomString(v));

    /// <summary>Constructs the custom element constructor with no arguments. The result must be an HTMLElement.</summary>
    internal override HtmlElement Construct(CustomElementDefinition definition, Document document, CustomElementRegistry? registry)
    {
        realm.Agent.Host.OnCallbackInvoking();
        TValue result;
        try
        {
            result = realm.Adapter.Construct(Function, [], Function);
        }
        finally
        {
            realm.Agent.Host.OnCallbackInvoked(realm.GlobalScope);
        }

        if (realm.Unwrap(result) is HtmlElement element)
            return element;

        throw ErrorRealm.CreateTypeError("The custom element constructor did not return an HTMLElement");
    }

    internal override void InvokeCallback(Element element, CustomElementCallbackKind kind, object callback, object?[] arguments)
    {
        var callbackFunction = (TValue)callback;
        var callbackRealm = realm.Agent.GetAssociatedRealm(callbackFunction) ?? realm;
        var jsArguments = new TValue[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            jsArguments[i] = callbackRealm.ToJs(arguments[i]);
        }

        // The exception is reported to the global object of the callback
        callbackRealm.InvokeCallback(callbackFunction, callbackRealm.ToJs(element), jsArguments, CallbackBehavior.ReportExceptions | CallbackBehavior.RunWhenInactive);
    }

    internal override void ReportException(Exception exception, Document? document)
    {
        var jsException = realm.ToJsException(exception);
        realm.Agent.ReportCallbackException(Function, realm, jsException);
    }
}

// Custom elements and the HTML element constructors
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>Returns the .NET custom element constructor of a JavaScript function (the same object for the same function).</summary>
    internal CustomElementConstructor ToCustomElementConstructor(TValue value)
    {
        if (!Adapter.IsCallable(value))
            throw CreateTypeError("The value is not of type 'CustomElementConstructor'");

        return Agent.CustomElementConstructors.GetValue(value, f => new JsCustomElementConstructor<TAdapter, TValue>(Agent.GetAssociatedRealm(f) ?? this, f));
    }

    /// <summary>Returns the JavaScript function of a custom element constructor, or undefined.</summary>
    internal TValue FromCustomElementConstructor(CustomElementConstructor? value) => value switch
    {
        JsCustomElementConstructor<TAdapter, TValue> constructor => constructor.Function,
        _ => Adapter.Undefined,
    };

    /// <summary>
    /// The overridden constructor steps of the interfaces with an [HTMLConstructor] constructor
    /// (https://html.spec.whatwg.org/multipage/dom.html#html-element-constructors).
    /// </summary>
    private TValue ConstructHtmlElement(InterfaceBinding<TAdapter, TValue> binding, TValue newTarget)
    {
        var activeFunction = EnsureInterfaceObject(binding).InterfaceObject;
        if (ReferenceEquals(newTarget, activeFunction) || !Adapter.IsObject(newTarget))
            throw CreateTypeError("Illegal constructor");

        var constructor = Agent.CustomElementConstructors.TryGetValue(newTarget, out var existing) ? existing : null;
        CustomElementRegistry? registry;
        if (constructor is not null && Html.CustomElementReactionsStack.Current.ActiveConstructorMap.TryGetValue(constructor, out var activeRegistry))
        {
            registry = activeRegistry;
        }
        else
        {
            registry = Window!.Document.CustomElementRegistryStorage;
        }

        var definition = constructor is null ? null : registry?.GetDefinition(constructor);
        if (definition is null)
            throw CreateTypeError("Illegal constructor: the constructor is not a custom element constructor of the registry");

        if (definition.IsAutonomous)
        {
            if (binding.Name != "HTMLElement")
                throw CreateTypeError("An autonomous custom element must extend HTMLElement");
        }
        else if (WebIdlBindings<TAdapter, TValue>.GetInterface(ElementFactory.GetHtmlElementInterface(definition.LocalName))?.Name != binding.Name)
        {
            throw CreateTypeError($"A customized built-in element extending '{definition.LocalName}' cannot extend {binding.Name}");
        }

        if (definition.ConstructionStack.Count == 0)
        {
            var created = CustomElementRegistry.CreateConstructedElement(definition, Window!.Document, registry);
            return GetWrapper(created, GetPrototypeFromConstructor(newTarget, binding));
        }

        var prototype = GetPrototypeFromConstructor(newTarget, binding);
        var element = definition.ConstructionStack[^1] ?? throw CreateTypeError("The custom element is already constructed");
        var wrapper = ToJs(element);
        Adapter.SetPrototypeOf(wrapper, prototype);
        definition.ConstructionStack[^1] = null;
        return wrapper;
    }

    /// <summary>https://webidl.spec.whatwg.org/#internally-create-a-new-object-implementing-the-interface (the prototype from NewTarget)</summary>
    private TValue GetPrototypeFromConstructor(TValue newTarget, InterfaceBinding<TAdapter, TValue> binding)
    {
        var prototype = Adapter.Get(newTarget, Key("prototype"));
        if (Adapter.IsObject(prototype))
            return prototype;

        var realm = Agent.GetAssociatedRealm(newTarget) ?? this;
        return realm.EnsureInterfaceObject(binding).Prototype;
    }
}
