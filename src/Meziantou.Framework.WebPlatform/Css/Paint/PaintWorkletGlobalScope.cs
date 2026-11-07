using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The global object of a paint worklet (https://drafts.css-houdini.org/css-paint-api-1/#paintworkletglobalscope): the
/// scripts added to <c>CSS.paintWorklet</c> register the classes that paint the <c>paint()</c> images.
/// </summary>
/// <seealso href="https://drafts.css-houdini.org/css-paint-api-1/#paintworkletglobalscope"/>
[IdlInterface("PaintWorkletGlobalScope", "https://drafts.css-houdini.org/css-paint-api-1/#paintworkletglobalscope")]
public sealed class PaintWorkletGlobalScope : WorkletGlobalScope
{
    internal PaintWorkletGlobalScope(PaintWorklet worklet)
        : base(worklet)
    {
        PaintWorklet = worklet;
    }

    internal PaintWorklet PaintWorklet { get; }

    internal override ExposureSet Exposure => ExposureSet.PaintWorklet;

    internal override string InterfaceName => "PaintWorkletGlobalScope";

    /// <summary>The paint definitions of the global scope (https://drafts.css-houdini.org/css-paint-api-1/#paint-definitions), by name.</summary>
    internal Dictionary<string, PaintDefinition> PaintDefinitions { get; } = new(StringComparer.Ordinal);

    /// <summary>The paint class instances of the global scope (https://drafts.css-houdini.org/css-paint-api-1/#paint-class-instances), by name.</summary>
    internal Dictionary<string, object> PaintClassInstances { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a class that paints the <c>paint(<paramref name="name"/>)</c> images
    /// (https://drafts.css-houdini.org/css-paint-api-1/#dom-paintworkletglobalscope-registerpaint).
    /// </summary>
    /// <param name="name">The name of the paint.</param>
    /// <param name="paintCtor">The class: its static <c>inputProperties</c>, <c>inputArguments</c> and <c>contextOptions</c> properties are read once, and its prototype has the <c>paint</c> callback.</param>
    /// <exception cref="TypeErrorException">The name is empty, an input argument is not a syntax definition, the value is not a constructor, or its prototype has no <c>paint</c> function.</exception>
    /// <exception cref="DomException">An <c>InvalidModificationError</c> when the name is already registered in the global scope.</exception>
    /// <seealso href="https://drafts.css-houdini.org/css-paint-api-1/#dom-paintworkletglobalscope-registerpaint"/>
    [IdlMember("registerPaint", "https://drafts.css-houdini.org/css-paint-api-1/#dom-paintworkletglobalscope-registerpaint")]
    public void RegisterPaint(string name, WorkletClassConstructor paintCtor)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(paintCtor);
        if (name.Length == 0)
            throw new TypeErrorException("The name of a paint cannot be empty");

        if (PaintDefinitions.ContainsKey(name))
            throw new DomException($"The paint '{name}' is already registered", "InvalidModificationError");

        // The input properties: the supported CSS properties and the custom properties
        var inputProperties = new List<string>();
        foreach (var property in paintCtor.GetStringSequence("inputProperties") ?? [])
        {
            if (CssPropertyRegistry.IsCustomProperty(property))
            {
                inputProperties.Add(property);
            }
            else if (CssPropertyRegistry.Get(InfraAscii.ToAsciiLowercase(property)) is not null)
            {
                inputProperties.Add(InfraAscii.ToAsciiLowercase(property));
            }
        }

        var inputArgumentSyntaxes = new List<CustomPropertySyntax>();
        var inputArgumentSyntaxTexts = new List<string>();
        foreach (var argument in paintCtor.GetStringSequence("inputArguments") ?? [])
        {
            // https://drafts.css-houdini.org/css-properties-values-api/#consume-a-syntax-definition
            inputArgumentSyntaxes.Add(CustomPropertySyntax.Parse(argument) ?? throw new TypeErrorException($"'{argument}' is not a syntax definition"));
            inputArgumentSyntaxTexts.Add(argument);
        }

        var alpha = paintCtor.GetDictionaryBoolean("contextOptions", "alpha", defaultValue: true);
        if (!paintCtor.IsConstructor)
            throw new TypeErrorException("The paint class is not a constructor");

        if (!paintCtor.HasPrototypeObject)
            throw new TypeErrorException("The prototype of the paint class is not an object");

        var paint = paintCtor.GetPrototypeFunction("paint");
        PaintDefinitions[name] = new PaintDefinition(paintCtor, paint, inputProperties, alpha);

        // The document paint definition is registered by a task
        var documentDefinition = new DocumentPaintDefinition(inputProperties, inputArgumentSyntaxes, inputArgumentSyntaxTexts, alpha);
        QueueGlobalTask(() => PaintWorklet.RegisterDocumentPaintDefinition(name, documentDefinition));
    }

    /// <summary>
    /// Gets the number of device pixels per CSS pixel of the document
    /// (https://drafts.css-houdini.org/css-paint-api-1/#dom-paintworkletglobalscope-devicepixelratio), as <c>window.devicePixelRatio</c>.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-paint-api-1/#dom-paintworkletglobalscope-devicepixelratio"/>
    [IdlMember("devicePixelRatio", "https://drafts.css-houdini.org/css-paint-api-1/#dom-paintworkletglobalscope-devicepixelratio")]
    public double DevicePixelRatio => Worklet.Document.RelevantWindow?.DevicePixelRatio ?? 1;
}

/// <summary>A paint definition (https://drafts.css-houdini.org/css-paint-api-1/#paint-definition): a class registered in a global scope.</summary>
internal sealed class PaintDefinition(WorkletClassConstructor classConstructor, object paintFunction, IReadOnlyList<string> inputProperties, bool alpha)
{
    public WorkletClassConstructor ClassConstructor { get; } = classConstructor;

    public object PaintFunction { get; } = paintFunction;

    public bool IsConstructorValid { get; set; } = true;

    public IReadOnlyList<string> InputProperties { get; } = inputProperties;

    /// <summary>The <c>alpha</c> member of the <c>PaintRenderingContext2DSettings</c> of the class.</summary>
    public bool Alpha { get; } = alpha;
}

/// <summary>
/// A document paint definition (https://drafts.css-houdini.org/css-paint-api-1/#document-paint-definition): what the document
/// knows about a registered paint.
/// </summary>
internal sealed class DocumentPaintDefinition(IReadOnlyList<string> inputProperties, IReadOnlyList<CustomPropertySyntax> inputArgumentSyntaxes, IReadOnlyList<string> inputArgumentSyntaxTexts, bool alpha)
{
    public IReadOnlyList<string> InputProperties { get; } = inputProperties;

    public IReadOnlyList<CustomPropertySyntax> InputArgumentSyntaxes { get; } = inputArgumentSyntaxes;

    public bool Alpha { get; } = alpha;

    /// <summary>Whether two definitions have the same input properties, input argument syntaxes and context settings.</summary>
    public bool IsEquivalent(DocumentPaintDefinition other)
        => Alpha == other.Alpha && InputProperties.SequenceEqual(other.InputProperties, StringComparer.Ordinal) && inputArgumentSyntaxTexts.SequenceEqual(other.GetInputArgumentSyntaxTexts(), StringComparer.Ordinal);

    private IReadOnlyList<string> GetInputArgumentSyntaxTexts() => inputArgumentSyntaxTexts;
}
