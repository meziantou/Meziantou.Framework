using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>The definition of a custom property registered by <see cref="Css.RegisterProperty(PropertyDefinition)"/> (https://drafts.css-houdini.org/css-properties-values-api/#dictdef-propertydefinition).</summary>
/// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dictdef-propertydefinition"/>
[IdlInterface("PropertyDefinition", "https://drafts.css-houdini.org/css-properties-values-api-1/#dictdef-propertydefinition")]
public sealed class PropertyDefinition
{
    /// <summary>The name of the custom property (e.g. "--my-color").</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dom-propertydefinition-name"/>
    [IdlMember("name", "https://drafts.css-houdini.org/css-properties-values-api-1/#dom-propertydefinition-name")]
    public required string Name { get; init; }

    /// <summary>The syntax of the values (e.g. "&lt;color&gt;" or "&lt;length&gt;+ | auto"), "*" for any value.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dom-propertydefinition-syntax"/>
    [IdlMember("syntax", "https://drafts.css-houdini.org/css-properties-values-api-1/#dom-propertydefinition-syntax")]
    public string Syntax { get; init; } = "*";

    /// <summary>Gets a value indicating whether the custom property inherits its value from its parent.</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dom-propertydefinition-inherits"/>
    [IdlMember("inherits", "https://drafts.css-houdini.org/css-properties-values-api-1/#dom-propertydefinition-inherits")]
    public bool Inherits { get; init; } = true;

    /// <summary>The initial value, required unless the syntax is "*". It must be computationally independent (e.g. 10px, but not 1em or var(--x)).</summary>
    /// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dom-propertydefinition-initialvalue"/>
    [IdlMember("initialValue", "https://drafts.css-houdini.org/css-properties-values-api-1/#dom-propertydefinition-initialvalue")]
    public string? InitialValue { get; init; }
}

/// <summary>https://drafts.css-houdini.org/css-properties-values-api/#the-registerproperty-function</summary>
public static partial class Css
{
    /// <summary>
    /// Registers a custom property in the document of the current global object
    /// (https://drafts.css-houdini.org/css-properties-values-api/#dom-css-registerproperty).
    /// </summary>
    /// <exception cref="DomException">The name or the syntax is invalid, the initial value does not match the syntax (SyntaxError), or the property is already registered (InvalidModificationError).</exception>
    /// <seealso href="https://drafts.css-houdini.org/css-properties-values-api-1/#dom-css-registerproperty"/>
    [IdlMember("registerProperty", "https://drafts.css-houdini.org/css-properties-values-api-1/#dom-css-registerproperty")]
    public static void RegisterProperty(PropertyDefinition definition) => RegisterProperty(Document.CurrentGlobalDocument, definition);

    /// <summary>Registers a custom property in a document (https://drafts.css-houdini.org/css-properties-values-api/#register-a-custom-property).</summary>
    /// <exception cref="DomException">The name or the syntax is invalid, the initial value does not match the syntax (SyntaxError), or the property is already registered (InvalidModificationError).</exception>
    [NotInIdl("Registers a property in a given document (the IDL operation uses the document of the current global object)")]
    public static void RegisterProperty(Document document, PropertyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Name);
        ArgumentNullException.ThrowIfNull(definition.Syntax);

        // A custom property name string (https://drafts.css-houdini.org/css-typed-om/#custom-property-name-string)
        var name = definition.Name;
        if (!name.StartsWith("--", StringComparison.Ordinal))
            throw new DomException($"'{name}' is not a custom property name", "SyntaxError");

        var propertySet = document.RegisteredPropertySet;
        if (propertySet.ContainsKey(name))
            throw new DomException($"The property '{name}' is already registered", "InvalidModificationError");

        var syntax = CustomPropertySyntax.Parse(definition.Syntax) ?? throw new DomException($"'{definition.Syntax}' is not a valid syntax", "SyntaxError");

        // As in the browsers (and the web-platform-tests), the initial value is required when the syntax is not universal
        var initialValue = definition.InitialValue;
        if (initialValue is null ? !syntax.IsUniversal : !syntax.IsValidInitialValue(initialValue))
            throw new DomException($"'{initialValue}' is not a valid initial value for the syntax '{definition.Syntax}'", "SyntaxError");

        propertySet.Add(name, new CustomPropertyRegistration(name, syntax, definition.Inherits, initialValue));
        CssStyleVersion.Increment(document);
    }
}
