using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>Content patching with template elements (https://html.spec.whatwg.org/multipage/scripting.html#prepare-content-patching).</summary>
internal static class ContentPatching
{
    /// <summary>
    /// https://html.spec.whatwg.org/multipage/scripting.html#prepare-content-patching. The content between the markers is
    /// removed, unless <paramref name="removeContent"/> is false (a buffered patch replaces it when the template is closed).
    /// </summary>
    public static bool Prepare(Node scope, HtmlTemplateElement template, bool removeContent = true)
    {
        var markerName = template.GetAttributeValue("for")!;
        if (markerName.Length == 0)
            return false;

        var (start, end) = FindMarkers(scope, markerName);
        if (start is null)
            return false;

        template.InsertionTarget = start.ParentNode;
        template.InsertionStartMarker = start;
        template.InsertionEndMarker = end;
        if (ReferenceEquals(start, end) || !removeContent)
            return true;

        RemoveContentBetweenMarkers(start, end);
        return true;
    }

    /// <summary>Removes the siblings that follow a start marker, until its end marker (or the last sibling).</summary>
    public static void RemoveContentBetweenMarkers(ProcessingInstruction start, ProcessingInstruction? end)
    {

        var removedNodes = new List<Node>();
        for (var currentNode = start.NextSibling; currentNode is not null; currentNode = currentNode.NextSibling)
        {
            if (ReferenceEquals(currentNode, end))
                break;

            removedNodes.Add(currentNode);
        }

        foreach (var node in removedNodes)
        {
            node.RemoveFromParent();
        }
    }

    /// <summary>
    /// Whether the value of the tentative <c>sanitize</c> attribute of a template that patches content enables the safe
    /// sanitization of its content: the attribute is a boolean attribute (the empty string or <c>sanitize</c>, ASCII
    /// case-insensitive); another value (e.g. <c>unsafe</c>) does not sanitize.
    /// </summary>
    public static bool IsSanitizing(string? value) => value is not null && (value.Length == 0 || Infra.InfraAscii.EqualsIgnoreAsciiCase(value, "sanitize"));

    /// <summary>
    /// Whether the tentative extensions of the content patching (the declarative partial updates: <c>buffer</c>, <c>sanitize</c>,
    /// <c>src</c>) are enabled for the nodes of a document (see <see cref="WebPlatformFeatures.DeclarativePartialUpdatesEnabled"/>).
    /// </summary>
    public static bool AreExtensionsEnabled(Document document)
        => (document.BrowsingContext ?? document.OriginDocument?.BrowsingContext)?.Options.EffectiveFeatures.DeclarativePartialUpdatesEnabled ?? true;

    /// <summary>https://html.spec.whatwg.org/multipage/scripting.html#find-markers</summary>
    private static (ProcessingInstruction? Start, ProcessingInstruction? End) FindMarkers(Node scope, string name)
    {
        for (var node = scope.FirstChild; node is not null; node = node.GetNextInTreeOrder(scope))
        {
            if (node is not ProcessingInstruction pi || pi.GetAttribute("name") != name)
                continue;

            if (pi.Target == "marker")
                return (pi, pi);

            if (pi.Target == "start")
            {
                var nestingLevel = 0;
                for (var sibling = pi.NextSibling; sibling is not null; sibling = sibling.NextSibling)
                {
                    if (sibling is not ProcessingInstruction siblingPi)
                        continue;

                    if (siblingPi.Target == "start")
                    {
                        nestingLevel++;
                    }

                    if (siblingPi.Target == "end")
                    {
                        if (nestingLevel == 0)
                            return (pi, siblingPi);

                        nestingLevel--;
                    }
                }

                return (pi, null);
            }
        }

        return (null, null);
    }
}

/// <summary>
/// The state of the tentative extensions of the content patching of a template element (the declarative partial updates,
/// https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md) while the parser has it on its
/// stack of open elements.
/// </summary>
internal sealed class ContentPatch
{
    /// <summary>Whether the content is buffered (the <c>buffer</c> attribute): parsed into the template contents, then inserted at once when the template is closed.</summary>
    public bool Buffered { get; init; }

    /// <summary>The markers of the target of a buffered patch (the content between them is replaced when the template is closed).</summary>
    public ProcessingInstruction? StartMarker { get; init; }

    public ProcessingInstruction? EndMarker { get; init; }

    /// <summary>Whether the content is sanitized (the <c>sanitize</c> attribute): the sanitizer of the parser before the template, restored when it is closed.</summary>
    public bool Sanitizes { get; init; }

    public SanitizerConfiguration? PreviousSanitizer { get; init; }

    /// <summary>Whether the template was removed while it buffered its content (see <see cref="HtmlTemplateElement.RemovingSteps"/>).</summary>
    public bool Canceled { get; set; }
}
