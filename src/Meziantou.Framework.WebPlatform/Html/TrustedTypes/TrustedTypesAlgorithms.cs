using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The element interfaces of the tables of the Trusted Types specification.</summary>
internal enum TrustedTypesElementInterface
{
    Other,
    HtmlIFrameElement,
    HtmlScriptElement,
    SvgScriptElement,
}

/// <summary>A row of the table of https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-data-for-attribute.</summary>
internal readonly record struct TrustedTypeAttributeData(TrustedTypeName Type, string Sink);

/// <summary>The algorithms of the Trusted Types specification (https://w3c.github.io/trusted-types/dist/spec/#algorithms).</summary>
internal static class TrustedTypesAlgorithms
{
    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-compliant-string-algorithm with a trusted type input
    /// and the "script" sink group.
    /// </summary>
    /// <param name="document">The document whose relevant global object is the global of the algorithm.</param>
    public static string GetCompliantString(TrustedTypeName expectedType, Document document, TrustedType input, string sink)
    {
        if (input.Name == expectedType)
            return input.Data;

        return GetCompliantString(expectedType, document, input.Data, sink);
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-compliant-string-algorithm with a string input and
    /// the "script" sink group.
    /// </summary>
    /// <param name="document">The document whose relevant global object is the global of the algorithm.</param>
    /// <exception cref="TypeErrorException">Trusted Types are required and the default policy did not convert the value.</exception>
    public static string GetCompliantString(TrustedTypeName expectedType, Document document, string input, string sink)
    {
        var directives = TrustedTypePolicyFactory.GetDirectives(document);
        if (!DoesSinkTypeRequireTrustedTypes(directives, includeReportOnlyPolicies: true))
            return input;

        var convertedInput = ProcessValueWithDefaultPolicy(expectedType, TrustedTypePolicyFactory.Get(document), input, sink);
        if (convertedInput is null)
        {
            // The violation is reported, and only blocked by an enforced policy (the directives may have been changed by the
            // default policy)
            if (!ShouldSinkTypeMismatchViolationBeBlocked(document, sink, input))
                return input;

            throw new TypeErrorException($"This document requires '{expectedType.GetIdlName()}' assignment ({sink})");
        }

        return convertedInput;
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-compliant-input with a string input.</summary>
    /// <param name="document">The document whose relevant global object is the global of the algorithm.</param>
    /// <param name="isHtmlDocument">Whether the document type is <c>"html"</c>: the parser options are not used in XML documents.</param>
    /// <exception cref="TypeErrorException">Trusted Types are required and neither the options nor the value are trusted.</exception>
    public static (string Html, FragmentParserOptions Options) GetCompliantInput(Document document, string input, FragmentParserOptions options, string sink, bool isHtmlDocument = true, bool optionsFromAuthor = true)
    {
        if (GetCompliantInputOptions(document, options, sink, isHtmlDocument, optionsFromAuthor, out var compliantOptions))
            return (input, compliantOptions);

        return (GetCompliantString(TrustedTypeName.TrustedHtml, document, input, sink), compliantOptions);
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-compliant-input with a <see cref="TrustedHtml"/> input.</summary>
    /// <param name="document">The document whose relevant global object is the global of the algorithm.</param>
    /// <param name="isHtmlDocument">Whether the document type is <c>"html"</c>: the parser options are not used in XML documents.</param>
    /// <exception cref="TypeErrorException">Trusted Types are required and the options are not trusted.</exception>
    public static (string Html, FragmentParserOptions Options) GetCompliantInput(Document document, TrustedHtml input, FragmentParserOptions options, string sink, bool isHtmlDocument = true, bool optionsFromAuthor = true)
    {
        GetCompliantInputOptions(document, options, sink, isHtmlDocument, optionsFromAuthor, out var compliantOptions);
        return (input.Data, compliantOptions);
    }

    /// <summary>The options of https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-compliant-input: returns true when they are <c>TrustedHTMLParserOptions</c>, which make any input compliant.</summary>
    private static bool GetCompliantInputOptions(Document document, FragmentParserOptions options, string sink, bool isHtmlDocument, bool optionsFromAuthor, out FragmentParserOptions compliantOptions)
    {
        compliantOptions = options;
        if (!isHtmlDocument)
            return false;

        compliantOptions = GetCompliantParserOptions(document, options, throwIfMissing: false, sink, optionsFromAuthor);
        return compliantOptions.Trusted is not null;
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-compliant-parser-options: the options, or the
    /// <c>TrustedHTMLParserOptions</c> created by the <c>createParserOptions</c> callback of the default policy when Trusted
    /// Types are required.
    /// </summary>
    /// <param name="document">The document whose relevant global object is the global of the algorithm.</param>
    /// <param name="throwIfMissing">Whether the options must be trusted (e.g. the streaming methods, whose HTML is not known up front).</param>
    /// <param name="optionsFromAuthor">Whether the options come from the author: the options that the sink creates (e.g. the <c>runScripts</c> of <c>createContextualFragment()</c>) do not need vetting.</param>
    /// <exception cref="TypeErrorException">The options must be trusted, and the default policy did not create them.</exception>
    public static FragmentParserOptions GetCompliantParserOptions(Document document, FragmentParserOptions input, bool throwIfMissing, string sink, bool optionsFromAuthor = true)
    {
        if (input.Trusted is not null)
            return input;

        if (!DoesSinkTypeRequireTrustedTypes(TrustedTypePolicyFactory.GetDirectives(document), includeReportOnlyPolicies: true))
            return input;

        var requirePolicy = throwIfMissing || (optionsFromAuthor && input.NeedsVetting);
        if (TrustedTypePolicyFactory.Get(document).DefaultPolicy is not { HasCreateParserOptions: true } defaultPolicy)
        {
            if (requirePolicy && ShouldSinkTypeMismatchViolationBeBlocked(document, sink, ""))
                throw new TypeErrorException($"This document requires 'TrustedHTMLParserOptions' ({sink})");

            return input;
        }

        var policyValue = defaultPolicy.InvokeCreateParserOptions(input.RunScripts, input.Sanitizer, ["TrustedHTMLParserOptions", sink]);
        if (policyValue is null)
        {
            // The violation is reported, and only blocked by an enforced policy
            if (!ShouldSinkTypeMismatchViolationBeBlocked(document, sink, ""))
                return input;

            throw new TypeErrorException($"This document requires 'TrustedHTMLParserOptions' ({sink})");
        }

        // The callback of the default policy determines whether the scripts run, whatever the options of the caller
        return new FragmentParserOptions(Sanitizer: null, RunScripts: false, CreateParserOptions(policyValue));
    }

    /// <summary>
    /// The <see cref="TrustedHtmlParserOptions"/> of the options returned by a <c>createParserOptions</c> callback
    /// (https://w3c.github.io/trusted-types/dist/spec/#create-a-trusted-type-algorithm): a copy of the configuration of
    /// their sanitizer (https://w3c.github.io/trusted-types/dist/spec/#get-a-sanitizer-config-copy-from-dictionary) and their
    /// <c>runScripts</c>.
    /// </summary>
    public static TrustedHtmlParserOptions CreateParserOptions(SetHtmlUnsafeOptions policyValue)
    {
        var sanitizer = policyValue.Sanitizer is { } spec ? Sanitizer.FromDictionary(spec) : null;
        return new TrustedHtmlParserOptions(sanitizer?.Configuration, policyValue.RunScripts);
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#create-a-parser-options-callback-argument: the options given to a
    /// <c>createParserOptions</c> callback, whose sanitizer (if any) is a new <see cref="Sanitizer"/>.
    /// </summary>
    public static SetHtmlUnsafeOptions CreateParserOptionsCallbackArgument(bool runScripts, SanitizerOrSanitizerConfigOrSanitizerPresets? sanitizer) => new()
    {
        RunScripts = runScripts,
        Sanitizer = sanitizer is { } spec ? Sanitizer.FromDictionary(spec) : (SanitizerOrSanitizerConfigOrSanitizerPresets?)null,
    };

    /// <summary>
    /// Whether a value that is not a trusted type is blocked by the Trusted Types directives of the document (without a
    /// default policy): the violations are reported, with the sample <paramref name="source"/>.
    /// </summary>
    public static bool IsSinkTypeMismatchBlocked(Document document, string sink, string source)
    {
        var directives = TrustedTypePolicyFactory.GetDirectives(document);
        return DoesSinkTypeRequireTrustedTypes(directives, includeReportOnlyPolicies: true) && ShouldSinkTypeMismatchViolationBeBlocked(document, sink, source);
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#process-value-with-a-default-policy-algorithm: returns the data of the
    /// trusted type created by the default policy, or null.
    /// </summary>
    public static string? ProcessValueWithDefaultPolicy(TrustedTypeName expectedType, TrustedTypePolicyFactory global, string input, string sink)
    {
        if (global.DefaultPolicy is not { } defaultPolicy)
            return null;

        var trustedTypeName = expectedType.GetIdlName();
        return defaultPolicy.GetPolicyValue(expectedType, input, [trustedTypeName, sink], throwIfMissing: false);
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-compliant-attribute-value
    /// </summary>
    /// <param name="newValue">A <see cref="string"/> or a <see cref="TrustedType"/>.</param>
    public static string GetCompliantAttributeValue(string attributeName, string? attributeNs, Element element, object newValue)
    {
        if (attributeNs is { Length: 0 })
        {
            attributeNs = null;
        }

        if (GetDataForAttribute(GetElementInterface(element), element.NamespaceUri, attributeName, attributeNs) is not { } attributeData)
            return newValue is TrustedType trustedValue ? trustedValue.Data : (string)newValue;

        return newValue is TrustedType trusted
            ? GetCompliantString(attributeData.Type, element.NodeDocument, trusted, attributeData.Sink)
            : GetCompliantString(attributeData.Type, element.NodeDocument, (string)newValue, attributeData.Sink);
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-data-for-attribute</summary>
    public static TrustedTypeAttributeData? GetDataForAttribute(TrustedTypesElementInterface element, string? elementNamespace, string attribute, string? attributeNs)
    {
        if (attributeNs is null && elementNamespace is Namespaces.Html or Namespaces.Svg or Namespaces.MathML && IsEventHandlerContentAttribute(attribute))
            return new TrustedTypeAttributeData(TrustedTypeName.TrustedScript, "Element " + attribute);

        return (element, attributeNs, attribute) switch
        {
            (TrustedTypesElementInterface.HtmlIFrameElement, null, "srcdoc") => new TrustedTypeAttributeData(TrustedTypeName.TrustedHtml, "HTMLIFrameElement srcdoc"),
            (TrustedTypesElementInterface.HtmlScriptElement, null, "src") => new TrustedTypeAttributeData(TrustedTypeName.TrustedScriptUrl, "HTMLScriptElement src"),
            (TrustedTypesElementInterface.SvgScriptElement, null or Namespaces.XLink, "href") => new TrustedTypeAttributeData(TrustedTypeName.TrustedScriptUrl, "SVGScriptElement href"),
            _ => null,
        };
    }

    /// <summary>
    /// Whether the attribute is the name of an event handler content attribute
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#event-handler-content-attributes). The specification does not
    /// define the list (https://github.com/w3c/trusted-types/issues/520): the event handlers of the elements (including the
    /// ones of body and frameset forwarded to the window) are used, whatever the element.
    /// </summary>
    private static bool IsEventHandlerContentAttribute(string attribute)
    {
        if (attribute.Length <= 2 || !attribute.StartsWith("on", StringComparison.Ordinal))
            return false;

        var type = attribute[2..];
        return EventHandlerNames.Elements.Contains(type) || EventHandlerNames.WindowReflecting.Contains(type);
    }

    /// <summary>The element interface for a local name and a namespace (https://dom.spec.whatwg.org/#concept-element-interface), restricted to the interfaces of the Trusted Types tables.</summary>
    public static TrustedTypesElementInterface GetElementInterface(string localName, string? @namespace) => (@namespace, localName) switch
    {
        (Namespaces.Html, "iframe") => TrustedTypesElementInterface.HtmlIFrameElement,
        (Namespaces.Html, "script") => TrustedTypesElementInterface.HtmlScriptElement,
        (Namespaces.Svg, "script") => TrustedTypesElementInterface.SvgScriptElement,
        _ => TrustedTypesElementInterface.Other,
    };

    private static TrustedTypesElementInterface GetElementInterface(Element element) => element switch
    {
        HtmlIFrameElement => TrustedTypesElementInterface.HtmlIFrameElement,
        HtmlScriptElement => TrustedTypesElementInterface.HtmlScriptElement,
        SvgScriptElement => TrustedTypesElementInterface.SvgScriptElement,
        _ => TrustedTypesElementInterface.Other,
    };

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#does-sink-type-require-trusted-types (the "script" sink group)</summary>
    public static bool DoesSinkTypeRequireTrustedTypes(List<TrustedTypesDirectives>? directives, bool includeReportOnlyPolicies)
    {
        if (directives is null)
            return false;

        foreach (var policy in directives)
        {
            if (!policy.RequireTrustedTypesForScript)
                continue;

            if (!policy.ReportOnly || includeReportOnlyPolicies)
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#require-trusted-types-for-pre-navigation-check: the pre-navigation check of
    /// the <c>require-trusted-types-for</c> directives of the policies of <paramref name="document"/> for a navigation to the
    /// <c>javascript:</c> URL <paramref name="url"/> (serialized). The default policy may convert the script, which changes the
    /// URL of the request. When it does not (no default policy, an exception, or a converted URL that cannot be parsed), the
    /// navigation violates the directives: the violations are reported, and the navigation is blocked by an enforced policy (a
    /// report-only policy navigates to the original URL).
    /// </summary>
    public static bool ShouldJavaScriptUrlNavigationBeBlocked(Document document, ref string url)
    {
        if (!DoesSinkTypeRequireTrustedTypes(TrustedTypePolicyFactory.GetDirectives(document), includeReportOnlyPolicies: true))
            return false;

        var encodedScriptSource = url["javascript:".Length..];
        string? convertedScriptSource;
        try
        {
            convertedScriptSource = ProcessValueWithDefaultPolicy(TrustedTypeName.TrustedScript, TrustedTypePolicyFactory.Get(document), encodedScriptSource, "Location href");
        }
#pragma warning disable CA1031 // An exception thrown by the default policy is a violation
        catch (Exception)
#pragma warning restore CA1031
        {
            convertedScriptSource = null;
        }

        if (convertedScriptSource is not null && Urls.UrlParser.Parse("javascript:" + convertedScriptSource, baseUrl: null) is { } convertedUrl)
        {
            url = convertedUrl.Serialize();
            return false;
        }

        return ShouldSinkTypeMismatchViolationBeBlocked(document, "Location href", encodedScriptSource);
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#should-block-sink-type-mismatch (the "script" sink group): a violation
    /// of the <c>require-trusted-types-for</c> directive is reported for each policy that contains it, and blocks the value
    /// when the policy is enforced.
    /// </summary>
    /// <param name="document">The document whose relevant global object is the global of the algorithm.</param>
    /// <param name="sink">The name of the sink (e.g. <c>Element innerHTML</c>).</param>
    /// <param name="source">The value assigned to the sink.</param>
    public static bool ShouldSinkTypeMismatchViolationBeBlocked(Document document, string sink, string source)
    {
        if (TrustedTypePolicyFactory.GetDirectives(document) is not { } directives)
            return false;

        var sample = source;
        if (sink == "Function")
        {
            foreach (var prefix in (ReadOnlySpan<string>)["function anonymous", "async function anonymous", "function* anonymous", "async function* anonymous"])
            {
                if (sample.StartsWith(prefix, StringComparison.Ordinal))
                {
                    sample = sample[prefix.Length..];
                    break;
                }
            }
        }

        var global = TrustedTypePolicyFactory.GetGlobalDocument(document);
        var result = false;
        foreach (var directive in directives)
        {
            if (!directive.RequireTrustedTypesForScript)
                continue;

            var violation = ContentSecurityPolicyViolation.Create(global, directive.Policy, "require-trusted-types-for");
            violation.Resource = "trusted-types-sink";
            violation.Sample = sink + "|" + ContentSecurityPolicyViolation.ClipSample(sample);
            violation.Report();
            if (!directive.ReportOnly)
            {
                result = true;
            }
        }

        return result;
    }

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#should-block-create-policy: a violation of the <c>trusted-types</c>
    /// directive is reported for each policy that does not allow the policy name, and blocks the creation when the policy
    /// is enforced.
    /// </summary>
    public static bool ShouldPolicyCreationBeBlocked(TrustedTypePolicyFactory global, string policyName, List<string> createdPolicyNames)
    {
        if (TrustedTypePolicyFactory.GetDirectives(global.Document) is not { } directives)
            return false;

        var globalDocument = TrustedTypePolicyFactory.GetGlobalDocument(global.Document);
        var result = false;
        foreach (var directive in directives)
        {
            if (!directive.IsPolicyCreationViolation(policyName, createdPolicyNames))
                continue;

            var violation = ContentSecurityPolicyViolation.Create(globalDocument, directive.Policy, "trusted-types");
            violation.Resource = "trusted-types-policy";
            violation.Sample = ContentSecurityPolicyViolation.ClipSample(policyName);
            violation.Report();
            if (!directive.ReportOnly)
            {
                result = true;
            }
        }

        return result;
    }
}
