using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

internal static partial class ElementFactory
{
    /// <summary>https://dom.spec.whatwg.org/#concept-create-element, with the registry of the document.</summary>
    public static Element CreateElement(Document document, string localName, string? @namespace, string? prefix, string? isValue, bool synchronousCustomElements)
        => CreateElement(document, localName, @namespace, prefix, isValue, synchronousCustomElements, CustomElementRegistry.Default);

    /// <summary>
    /// https://dom.spec.whatwg.org/#concept-create-element. <paramref name="registry"/> is <see cref="CustomElementRegistry.Default"/>
    /// for the registry of the document ("default"), null, or a registry.
    /// </summary>
    public static Element CreateElement(Document document, string localName, string? @namespace, string? prefix, string? isValue, bool synchronousCustomElements, CustomElementRegistry? registry)
    {
        @namespace = Namespaces.Intern(@namespace);
        if (ReferenceEquals(registry, CustomElementRegistry.Default))
        {
            registry = document.CustomElementRegistryStorage;
        }

        if (registry is { HasAnyDefinition: true } && CustomElementRegistry.LookUpDefinition(registry, @namespace, localName, isValue) is { } definition)
            return CreateCustomElement(document, localName, prefix, isValue, synchronousCustomElements, registry, definition);

        Element result;
        if (ReferenceEquals(@namespace, Namespaces.Html))
        {
            result = CreateHtmlElement(document, localName, prefix);
            if (isValue is not null || CustomElements.IsValidCustomElementName(localName))
            {
                result.CustomElementState = CustomElementState.Undefined;
            }
        }
        else if (ReferenceEquals(@namespace, Namespaces.Svg))
        {
            result = CreateSvgElementOrNull(document, localName, prefix) ?? new SvgElement(document, localName, prefix);
        }
        else if (ReferenceEquals(@namespace, Namespaces.MathML))
        {
            result = CreateMathMLElementOrNull(document, localName, prefix) ?? new MathMLElement(document, localName, prefix);
        }
        else
        {
            result = new Element(document, localName, @namespace, prefix);
        }

        result.IsValue = isValue;
        if (registry is not null)
        {
            result.CustomElementRegistry = registry;
        }

        return result;
    }

    /// <summary>https://dom.spec.whatwg.org/#concept-create-element (steps 4 and 5: the element has a custom element definition).</summary>
    private static Element CreateCustomElement(Document document, string localName, string? prefix, string? isValue, bool synchronousCustomElements, CustomElementRegistry registry, CustomElementDefinition definition)
    {
        if (!definition.IsAutonomous)
        {
            // A customized built-in element
            var builtIn = CreateHtmlElement(document, localName, prefix);
            builtIn.CustomElementRegistry = registry;
            builtIn.CustomElementState = CustomElementState.Undefined;
            builtIn.IsValue = isValue;
            if (synchronousCustomElements)
            {
                try
                {
                    CustomElementReactions.Upgrade(builtIn, definition);
                }
#pragma warning disable CA1031 // "report the exception"
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    definition.Constructor.ReportException(ex, document);
                    builtIn.CustomElementState = CustomElementState.Failed;
                }
            }
            else
            {
                CustomElementReactions.EnqueueUpgradeReaction(builtIn, definition);
            }

            return builtIn;
        }

        if (!synchronousCustomElements)
        {
            var element = new HtmlElement(document, localName, prefix)
            {
                CustomElementState = CustomElementState.Undefined,
            };
            element.CustomElementRegistry = registry;
            CustomElementReactions.EnqueueUpgradeReaction(element, definition);
            return element;
        }

        var constructor = definition.Constructor;
        var activeConstructorMap = CustomElementReactionsStack.Current.ActiveConstructorMap;
        var hasPreviousRegistry = activeConstructorMap.TryGetValue(constructor, out var previousRegistry);
        activeConstructorMap[constructor] = registry;
        try
        {
            HtmlElement result;
            using (AuthorCodeScope.Enter())
            {
                result = constructor.Construct(definition, document, registry);
            }

            if (result.AttributeCount > 0)
                throw DomException.NotSupported("The custom element constructor must not add attributes");

            if (result.HasChildNodes())
                throw DomException.NotSupported("The custom element constructor must not add children");

            if (result.ParentNode is not null)
                throw DomException.NotSupported("The custom element constructor must not insert the element");

            if (!ReferenceEquals(result.NodeDocument, document))
                throw DomException.NotSupported("The custom element constructor must create the element in the document");

            if (result.LocalName != localName)
                throw DomException.NotSupported("The custom element constructor must create an element with the local name of the definition");

            result.Prefix = prefix;
            result.IsValue = null;
            result.CustomElementRegistry = registry;
            return result;
        }
#pragma warning disable CA1031 // "report the exception"
        catch (Exception ex)
#pragma warning restore CA1031
        {
            constructor.ReportException(ex, document);
            var failed = new HtmlUnknownElement(document, localName, prefix)
            {
                CustomElementState = CustomElementState.Failed,
            };
            failed.CustomElementRegistry = registry;
            return failed;
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
        }
    }

    /// <summary>Creates an element implementing the element interface for the local name (https://html.spec.whatwg.org/#element-interface).</summary>
    private static HtmlElement CreateHtmlElement(Document document, string localName, string? prefix)
    {
        var element = CreateHtmlElementOrNull(document, localName, prefix);
        if (element is not null)
            return element;

        // Valid custom element names use HTMLElement; other unknown names use HTMLUnknownElement
        return CustomElements.IsValidCustomElementName(localName) ? new HtmlElement(document, localName, prefix) : new HtmlUnknownElement(document, localName, prefix);
    }

    /// <summary>Creates an HTML element implementing the element interface for the local name, without custom element state.</summary>
    internal static HtmlElement CreateHtmlElementForInterface(Document document, string localName) => CreateHtmlElement(document, localName, prefix: null);

    /// <summary>Whether the element interface for the local name and the HTML namespace is HTMLUnknownElement.</summary>
    internal static bool IsUnknownHtmlElement(string localName) => CreateHtmlElement(Document.CurrentGlobalDocument, localName, prefix: null) is HtmlUnknownElement;

    /// <summary>Returns the .NET type of the element interface for the local name and the HTML namespace.</summary>
    internal static Type GetHtmlElementInterface(string localName) => CreateHtmlElement(Document.CurrentGlobalDocument, localName, prefix: null).GetType();
}
