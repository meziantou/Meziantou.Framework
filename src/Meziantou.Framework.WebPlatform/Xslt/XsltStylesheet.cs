using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>
/// A compiled XSLT 1.0 stylesheet (https://www.w3.org/TR/xslt-10/), with its imported and included stylesheets. It is
/// independent of the DOM nodes it was compiled from, and can run several transformations.
/// </summary>
internal sealed class XsltStylesheet
{
    /// <summary>The namespace of XSLT (https://www.w3.org/TR/xslt-10/#xslt-namespace).</summary>
    public const string XsltNamespace = "http://www.w3.org/1999/XSL/Transform";

    /// <summary>The key of the default mode in <see cref="TemplateRules"/>.</summary>
    public static readonly XsltQName DefaultMode = new("", "");

    /// <summary>The template rules of each mode, from the one that has the highest priority to the lowest (https://www.w3.org/TR/xslt-10/#conflict).</summary>
    public Dictionary<XsltQName, List<XsltTemplateRule>> TemplateRules { get; } = [];

    public Dictionary<XsltQName, XsltTemplate> NamedTemplates { get; } = [];

    public Dictionary<XsltQName, XsltGlobalVariable> GlobalVariables { get; } = [];

    public Dictionary<XsltQName, List<XsltKeyDefinition>> Keys { get; } = [];

    /// <summary>The decimal formats; the default one is named <see cref="DefaultMode"/>.</summary>
    public Dictionary<XsltQName, XsltDecimalFormat> DecimalFormats { get; } = [];

    public Dictionary<XsltQName, XsltAttributeSet> AttributeSets { get; } = [];

    public XsltOutputSettings Output { get; } = new();

    /// <summary>The xsl:strip-space and xsl:preserve-space declarations (https://www.w3.org/TR/xslt-10/#strip).</summary>
    public List<XsltSpaceRule> SpaceRules { get; } = [];

    /// <summary>The documents of the stylesheet modules by URL, returned by document('') (https://www.w3.org/TR/xslt-10/#function-document).</summary>
    public Dictionary<string, Node> ModuleDocuments { get; } = new(StringComparer.Ordinal);

    public bool HasStripSpace => SpaceRules.Exists(rule => rule.Strip);
}

/// <summary>A template (https://www.w3.org/TR/xslt-10/#section-Defining-Template-Rules).</summary>
internal sealed class XsltTemplate
{
    public XsltPattern? Match { get; init; }

    public XsltQName? Name { get; init; }

    public XsltQName Mode { get; init; } = XsltStylesheet.DefaultMode;

    public int Precedence { get; init; }

    /// <summary>The lowest import precedence of the stylesheets imported by the stylesheet of the template (xsl:apply-imports uses the templates of [ImportLow, Precedence - 1]).</summary>
    public int ImportLow { get; init; }

    public XsltParam[] Parameters { get; set; } = [];

    public XsltInstruction[] Body { get; set; } = [];

    /// <summary>A description of the template, for the error messages.</summary>
    public string Description => Name is { } name ? $"template '{name}'" : $"template matching '{Match?.Text}'";
}

/// <summary>An alternative of the pattern of a template, with its priority.</summary>
internal sealed record XsltTemplateRule(XsltTemplate Template, XsltPatternAlternative Alternative, double Priority, int Position);

/// <summary>An xsl:param of a template, or a global xsl:param or xsl:variable.</summary>
internal sealed class XsltParam
{
    public required XsltQName Name { get; init; }

    public XsltExpression? Select { get; init; }

    public XsltInstruction[] Content { get; init; } = [];
}

/// <summary>A global variable or parameter (https://www.w3.org/TR/xslt-10/#top-level-variables).</summary>
internal sealed class XsltGlobalVariable
{
    public required XsltParam Definition { get; init; }

    public bool IsParameter { get; init; }

    public int Precedence { get; init; }
}

/// <summary>An xsl:key declaration (https://www.w3.org/TR/xslt-10/#key).</summary>
internal sealed record XsltKeyDefinition(XsltPattern Match, XsltExpression Use);

/// <summary>An xsl:attribute-set declaration (https://www.w3.org/TR/xslt-10/#attribute-sets): the merged declarations of the set.</summary>
internal sealed class XsltAttributeSet
{
    public List<XsltQName> UsedSets { get; } = [];

    public List<XsltInstruction> Attributes { get; } = [];
}

/// <summary>An xsl:strip-space or xsl:preserve-space name test.</summary>
internal sealed record XsltSpaceRule(bool Strip, string? NamespaceUri, string? LocalName, int Precedence, double Priority)
{
    public bool Matches(string namespaceUri, string localName) => (NamespaceUri is null || namespaceUri == NamespaceUri) && (LocalName is null || localName == LocalName);
}

/// <summary>The xsl:output declarations (https://www.w3.org/TR/xslt-10/#output), merged.</summary>
internal sealed class XsltOutputSettings
{
    /// <summary>"xml", "html", "text", another QName, or null when the method is determined from the result tree.</summary>
    public string? Method { get; set; }

    public string? Version { get; set; }

    public string? Encoding { get; set; }

    public bool? OmitXmlDeclaration { get; set; }

    public string? Standalone { get; set; }

    public string? DoctypePublic { get; set; }

    public string? DoctypeSystem { get; set; }

    public List<XsltQName> CdataSectionElements { get; } = [];

    public bool? Indent { get; set; }

    public string? MediaType { get; set; }
}

/// <summary>An xsl:decimal-format declaration (https://www.w3.org/TR/xslt-10/#format-number).</summary>
internal sealed record XsltDecimalFormat
{
    public char DecimalSeparator { get; init; } = '.';

    public char GroupingSeparator { get; init; } = ',';

    public string Infinity { get; init; } = "Infinity";

    public char MinusSign { get; init; } = '-';

    public string NaN { get; init; } = "NaN";

    public char Percent { get; init; } = '%';

    public char PerMille { get; init; } = '‰';

    public char ZeroDigit { get; init; } = '0';

    public char Digit { get; init; } = '#';

    public char PatternSeparator { get; init; } = ';';
}
