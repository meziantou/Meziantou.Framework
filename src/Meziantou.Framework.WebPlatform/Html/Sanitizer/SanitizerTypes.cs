namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizerpresets</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizerpresets"/>
[IdlInterface("SanitizerPresets", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitizerpresets")]
public enum SanitizerPresets
{
    /// <summary>The built-in safe default configuration.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerpresets-default"/>
    [IdlMember("default", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerpresets-default")]
    Default,
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerelementnamespace</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerelementnamespace"/>
[IdlInterface("SanitizerElementNamespace", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerelementnamespace")]
public class SanitizerElementNamespace
{
    /// <summary>Gets the local name of the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerelementnamespace-name"/>
    [IdlMember("name", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerelementnamespace-name")]
    public required string Name { get; init; }

    /// <summary>Gets the namespace of the element, the HTML namespace by default.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerelementnamespace-namespace"/>
    [IdlMember("namespace", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerelementnamespace-namespace")]
    public string? Namespace { get; init; } = Namespaces.Html;
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerelementnamespacewithattributes</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerelementnamespacewithattributes"/>
[IdlInterface("SanitizerElementNamespaceWithAttributes", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerelementnamespacewithattributes")]
public sealed class SanitizerElementNamespaceWithAttributes : SanitizerElementNamespace
{
    /// <summary>Gets the attributes to allow on the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerelementnamespacewithattributes-attributes"/>
    [IdlMember("attributes", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerelementnamespacewithattributes-attributes")]
    public IReadOnlyList<SanitizerAttribute>? Attributes { get; init; }

    /// <summary>Gets the attributes to remove from the element.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerelementnamespacewithattributes-removeattributes"/>
    [IdlMember("removeAttributes", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerelementnamespacewithattributes-removeattributes")]
    public IReadOnlyList<SanitizerAttribute>? RemoveAttributes { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerattributenamespace</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerattributenamespace"/>
[IdlInterface("SanitizerAttributeNamespace", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerattributenamespace")]
public sealed class SanitizerAttributeNamespace
{
    /// <summary>Gets the local name of the attribute.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerattributenamespace-name"/>
    [IdlMember("name", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerattributenamespace-name")]
    public required string Name { get; init; }

    /// <summary>Gets the namespace of the attribute, or null when the attribute has no namespace.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerattributenamespace-namespace"/>
    [IdlMember("namespace", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerattributenamespace-namespace")]
    public string? Namespace { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerprocessinginstruction</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerprocessinginstruction"/>
[IdlInterface("SanitizerProcessingInstruction", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerprocessinginstruction")]
public sealed class SanitizerProcessingInstruction
{
    /// <summary>Gets the target of the processing instruction.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerprocessinginstruction-target"/>
    [IdlMember("target", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerprocessinginstruction-target")]
    public required string Target { get; init; }
}

/// <summary>
/// https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerconfig. A member that is null
/// does not exist.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerconfig"/>
[IdlInterface("SanitizerConfig", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sanitizerconfig")]
public sealed class SanitizerConfig
{
    /// <summary>Gets the elements to allow, with their allowed or removed attributes.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-elements"/>
    [IdlMember("elements", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-elements")]
    public IReadOnlyList<SanitizerElementWithAttributes>? Elements { get; init; }

    /// <summary>Gets the elements to remove with their content.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-removeelements"/>
    [IdlMember("removeElements", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-removeelements")]
    public IReadOnlyList<SanitizerElement>? RemoveElements { get; init; }

    /// <summary>Gets the elements to replace with their children.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-replacewithchildrenelements"/>
    [IdlMember("replaceWithChildrenElements", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-replacewithchildrenelements")]
    public IReadOnlyList<SanitizerElement>? ReplaceWithChildrenElements { get; init; }

    /// <summary>Gets the processing instructions to allow.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-processinginstructions"/>
    [IdlMember("processingInstructions", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-processinginstructions")]
    public IReadOnlyList<SanitizerPI>? ProcessingInstructions { get; init; }

    /// <summary>Gets the processing instructions to remove.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-removeprocessinginstructions"/>
    [IdlMember("removeProcessingInstructions", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-removeprocessinginstructions")]
    public IReadOnlyList<SanitizerPI>? RemoveProcessingInstructions { get; init; }

    /// <summary>Gets the attributes to allow.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-attributes"/>
    [IdlMember("attributes", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-attributes")]
    public IReadOnlyList<SanitizerAttribute>? Attributes { get; init; }

    /// <summary>Gets the attributes to remove.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-removeattributes"/>
    [IdlMember("removeAttributes", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-removeattributes")]
    public IReadOnlyList<SanitizerAttribute>? RemoveAttributes { get; init; }

    /// <summary>Gets a value indicating whether comments are allowed.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-comments"/>
    [IdlMember("comments", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-comments")]
    public bool? Comments { get; init; }

    /// <summary>Gets a value indicating whether custom <c>data-*</c> attributes are allowed.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-dataattributes"/>
    [IdlMember("dataAttributes", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-dataattributes")]
    public bool? DataAttributes { get; init; }

    /// <summary>Gets a value indicating whether <c>javascript:</c> URLs are allowed.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-javascripturls"/>
    [IdlMember("javascriptURLs", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sanitizerconfig-javascripturls")]
    public bool? JavaScriptUrls { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sethtmloptions</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sethtmloptions"/>
[IdlInterface("SetHTMLOptions", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dictdef-sethtmloptions")]
public sealed class SetHtmlOptions
{
    /// <summary>Gets the sanitizer, configuration or preset to use; the default preset when not specified.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sethtmloptions-sanitizer"/>
    [IdlMember("sanitizer", "https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#dom-sethtmloptions-sanitizer")]
    public SanitizerOrSanitizerConfigOrSanitizerPresets Sanitizer { get; init; } = SanitizerPresets.Default;
}

/// <summary>The (SanitizerConfig or SanitizerPresets) union.</summary>
public readonly struct SanitizerConfigOrSanitizerPresets : IUnionValue, IEquatable<SanitizerConfigOrSanitizerPresets>
{
    private SanitizerConfigOrSanitizerPresets(object value) => Value = value;

    /// <summary>Gets the value of the union: a sanitizer configuration or a sanitizer preset.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as a sanitizer configuration, or null when the union holds another type.</summary>
    [NotInIdl]
    public SanitizerConfig? SanitizerConfig => Value as SanitizerConfig;

    /// <summary>Gets the value as a sanitizer preset, or null when the union holds another type.</summary>
    [NotInIdl]
    public SanitizerPresets? SanitizerPresets => Value is SanitizerPresets preset ? preset : null;

    /// <summary>Converts a sanitizer configuration to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerConfigOrSanitizerPresets(SanitizerConfig value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a sanitizer preset to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerConfigOrSanitizerPresets(SanitizerPresets value) => new(value);

    /// <summary>Creates the union from a sanitizer configuration.</summary>
    [NotInIdl]
    public static SanitizerConfigOrSanitizerPresets FromSanitizerConfig(SanitizerConfig value) => value;

    /// <summary>Creates the union from a sanitizer preset.</summary>
    [NotInIdl]
    public static SanitizerConfigOrSanitizerPresets FromSanitizerPresets(SanitizerPresets value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(SanitizerConfigOrSanitizerPresets other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SanitizerConfigOrSanitizerPresets other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(SanitizerConfigOrSanitizerPresets left, SanitizerConfigOrSanitizerPresets right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(SanitizerConfigOrSanitizerPresets left, SanitizerConfigOrSanitizerPresets right) => !left.Equals(right);
}

/// <summary>The (Sanitizer or SanitizerConfig or SanitizerPresets) union.</summary>
public readonly struct SanitizerOrSanitizerConfigOrSanitizerPresets : IUnionValue, IEquatable<SanitizerOrSanitizerConfigOrSanitizerPresets>
{
    private SanitizerOrSanitizerConfigOrSanitizerPresets(object value) => Value = value;

    /// <summary>Gets the value of the union: a sanitizer or a sanitizer configuration or a sanitizer preset.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as a sanitizer, or null when the union holds another type.</summary>
    [NotInIdl]
    public Sanitizer? Sanitizer => Value as Sanitizer;

    /// <summary>Gets the value as a sanitizer configuration, or null when the union holds another type.</summary>
    [NotInIdl]
    public SanitizerConfig? SanitizerConfig => Value as SanitizerConfig;

    /// <summary>Gets the value as a sanitizer preset, or null when the union holds another type.</summary>
    [NotInIdl]
    public SanitizerPresets? SanitizerPresets => Value is SanitizerPresets preset ? preset : null;

    /// <summary>Converts a sanitizer to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerOrSanitizerConfigOrSanitizerPresets(Sanitizer value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a sanitizer configuration to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerOrSanitizerConfigOrSanitizerPresets(SanitizerConfig value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a sanitizer preset to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerOrSanitizerConfigOrSanitizerPresets(SanitizerPresets value) => new(value);

    /// <summary>Creates the union from a sanitizer.</summary>
    [NotInIdl]
    public static SanitizerOrSanitizerConfigOrSanitizerPresets FromSanitizer(Sanitizer value) => value;

    /// <summary>Creates the union from a sanitizer configuration.</summary>
    [NotInIdl]
    public static SanitizerOrSanitizerConfigOrSanitizerPresets FromSanitizerConfig(SanitizerConfig value) => value;

    /// <summary>Creates the union from a sanitizer preset.</summary>
    [NotInIdl]
    public static SanitizerOrSanitizerConfigOrSanitizerPresets FromSanitizerPresets(SanitizerPresets value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(SanitizerOrSanitizerConfigOrSanitizerPresets other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SanitizerOrSanitizerConfigOrSanitizerPresets other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(SanitizerOrSanitizerConfigOrSanitizerPresets left, SanitizerOrSanitizerConfigOrSanitizerPresets right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(SanitizerOrSanitizerConfigOrSanitizerPresets left, SanitizerOrSanitizerConfigOrSanitizerPresets right) => !left.Equals(right);
}

/// <summary>The SanitizerElement typedef: (DOMString or SanitizerElementNamespace).</summary>
public readonly struct SanitizerElement : IUnionValue, IEquatable<SanitizerElement>
{
    private SanitizerElement(object value) => Value = value;

    /// <summary>Gets the value of the union: an element name or an element name with its namespace.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as an element name, or null when the union holds another type.</summary>
    [NotInIdl]
    public string? Name => Value as string;

    /// <summary>
    /// Gets the value as an element name with its namespace, or null when the union holds another type.
    /// </summary>
    [NotInIdl]
    public SanitizerElementNamespace? ElementNamespace => Value as SanitizerElementNamespace;

    /// <summary>Converts an element name to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerElement(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts an element name with its namespace to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerElement(SanitizerElementNamespace value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from an element name.</summary>
    [NotInIdl]
    public static SanitizerElement FromString(string value) => value;

    /// <summary>Creates the union from an element name with its namespace.</summary>
    [NotInIdl]
    public static SanitizerElement FromSanitizerElementNamespace(SanitizerElementNamespace value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(SanitizerElement other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SanitizerElement other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(SanitizerElement left, SanitizerElement right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(SanitizerElement left, SanitizerElement right) => !left.Equals(right);
}

/// <summary>The SanitizerElementWithAttributes typedef: (DOMString or SanitizerElementNamespaceWithAttributes).</summary>
public readonly struct SanitizerElementWithAttributes : IUnionValue, IEquatable<SanitizerElementWithAttributes>
{
    private SanitizerElementWithAttributes(object value) => Value = value;

    /// <summary>
    /// Gets the value of the union: an element name or an element name with its namespace and attributes.
    /// </summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as an element name, or null when the union holds another type.</summary>
    [NotInIdl]
    public string? Name => Value as string;

    /// <summary>
    /// Gets the value as an element name with its namespace and attributes, or null when the union holds another type.
    /// </summary>
    [NotInIdl]
    public SanitizerElementNamespaceWithAttributes? ElementNamespaceWithAttributes => Value as SanitizerElementNamespaceWithAttributes;

    /// <summary>Converts an element name to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerElementWithAttributes(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts an element name with its namespace and attributes to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerElementWithAttributes(SanitizerElementNamespaceWithAttributes value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from an element name.</summary>
    [NotInIdl]
    public static SanitizerElementWithAttributes FromString(string value) => value;

    /// <summary>Creates the union from an element name with its namespace and attributes.</summary>
    [NotInIdl]
    public static SanitizerElementWithAttributes FromSanitizerElementNamespaceWithAttributes(SanitizerElementNamespaceWithAttributes value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(SanitizerElementWithAttributes other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SanitizerElementWithAttributes other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(SanitizerElementWithAttributes left, SanitizerElementWithAttributes right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(SanitizerElementWithAttributes left, SanitizerElementWithAttributes right) => !left.Equals(right);
}

/// <summary>The SanitizerAttribute typedef: (DOMString or SanitizerAttributeNamespace).</summary>
public readonly struct SanitizerAttribute : IUnionValue, IEquatable<SanitizerAttribute>
{
    private SanitizerAttribute(object value) => Value = value;

    /// <summary>Gets the value of the union: an attribute name or an attribute name with its namespace.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as an attribute name, or null when the union holds another type.</summary>
    [NotInIdl]
    public string? Name => Value as string;

    /// <summary>
    /// Gets the value as an attribute name with its namespace, or null when the union holds another type.
    /// </summary>
    [NotInIdl]
    public SanitizerAttributeNamespace? AttributeNamespace => Value as SanitizerAttributeNamespace;

    /// <summary>Converts an attribute name to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerAttribute(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts an attribute name with its namespace to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerAttribute(SanitizerAttributeNamespace value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from an attribute name.</summary>
    [NotInIdl]
    public static SanitizerAttribute FromString(string value) => value;

    /// <summary>Creates the union from an attribute name with its namespace.</summary>
    [NotInIdl]
    public static SanitizerAttribute FromSanitizerAttributeNamespace(SanitizerAttributeNamespace value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(SanitizerAttribute other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SanitizerAttribute other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(SanitizerAttribute left, SanitizerAttribute right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(SanitizerAttribute left, SanitizerAttribute right) => !left.Equals(right);
}

/// <summary>The SanitizerPI typedef: (DOMString or SanitizerProcessingInstruction).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Name defined by the specification")]
public readonly struct SanitizerPI : IUnionValue, IEquatable<SanitizerPI>
{
    private SanitizerPI(object value) => Value = value;

    /// <summary>
    /// Gets the value of the union: a processing instruction target or a processing instruction description.
    /// </summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as a processing instruction target, or null when the union holds another type.</summary>
    [NotInIdl]
    public string? Target => Value as string;

    /// <summary>
    /// Gets the value as a processing instruction description, or null when the union holds another type.
    /// </summary>
    [NotInIdl]
    public SanitizerProcessingInstruction? ProcessingInstruction => Value as SanitizerProcessingInstruction;

    /// <summary>Converts a processing instruction target to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerPI(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a processing instruction description to the union.</summary>
    [NotInIdl]
    public static implicit operator SanitizerPI(SanitizerProcessingInstruction value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates the union from a processing instruction target.</summary>
    [NotInIdl]
    public static SanitizerPI FromString(string value) => value;

    /// <summary>Creates the union from a processing instruction description.</summary>
    [NotInIdl]
    public static SanitizerPI FromSanitizerProcessingInstruction(SanitizerProcessingInstruction value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(SanitizerPI other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SanitizerPI other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(SanitizerPI left, SanitizerPI right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(SanitizerPI left, SanitizerPI right) => !left.Equals(right);
}
