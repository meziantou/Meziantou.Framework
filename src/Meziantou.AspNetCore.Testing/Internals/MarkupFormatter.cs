using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>Formats DOM nodes for the snapshots: the nodes are cloned, the clones are updated, then formatted.</summary>
internal static class MarkupFormatter
{
    private static readonly HtmlPrettyFormatter PrettyFormatter = new();

    public static string Format(IReadOnlyList<Node> nodes, MarkupOptions? options)
    {
        options ??= MarkupOptions.Default;
        if (nodes.Count == 0)
            return "";

        var document = nodes[0].OwnerDocument ?? (Document)nodes[0];
        var fragment = document.CreateDocumentFragment();
        foreach (var node in nodes)
        {
            var clone = node.CloneNode(subtree: true);
            Transform(node, clone, options);
            fragment.AppendChild(clone);
        }

        // Blazor renders each expression in its own text node: merge the adjacent text nodes, as a user reads them
        fragment.Normalize();

        var formatter = options.Indent ? (HtmlFormatter)PrettyFormatter : HtmlFormatter.Default;
        return formatter.Format(fragment).Trim();
    }

    private static void Transform(Node original, Node clone, MarkupOptions options)
    {
        if (original is Element originalElement && clone is Element cloneElement)
        {
            if (options.RemoveElementReferenceAttributes || options.RemoveScopedCssAttributes)
            {
                foreach (var attribute in cloneElement.Attributes.ToArray())
                {
                    if ((options.RemoveElementReferenceAttributes && attribute.Name.StartsWith("_bl_", StringComparison.Ordinal))
                        || (options.RemoveScopedCssAttributes && IsScopedCssAttribute(attribute.Name)))
                    {
                        cloneElement.RemoveAttribute(attribute.Name);
                    }
                }
            }

            if (options.IncludeFormState)
            {
                switch (originalElement)
                {
                    case HtmlInputElement { Type: "checkbox" or "radio" } input:
                        SetBooleanAttribute(cloneElement, "checked", input.Checked);
                        break;

                    case HtmlInputElement { Type: not ("file" or "password") } input:
                        if (input.Value.Length > 0 || cloneElement.HasAttribute("value"))
                        {
                            cloneElement.SetAttribute("value", input.Value);
                        }

                        break;

                    case HtmlTextAreaElement textArea:
                        cloneElement.TextContent = textArea.Value;
                        break;

                    case HtmlOptionElement option:
                        SetBooleanAttribute(cloneElement, "selected", option.Selected);
                        break;
                }
            }
        }

        var originalChild = original.FirstChild;
        var cloneChild = clone.FirstChild;
        while (originalChild is not null && cloneChild is not null)
        {
            Transform(originalChild, cloneChild, options);
            originalChild = originalChild.NextSibling;
            cloneChild = cloneChild.NextSibling;
        }
    }

    private static void SetBooleanAttribute(Element element, string name, bool value)
    {
        if (value)
        {
            element.SetAttribute(name, "");
        }
        else
        {
            element.RemoveAttribute(name);
        }
    }

    // The attribute of the CSS isolation is "b-" followed by 10 lowercase letters or digits
    private static bool IsScopedCssAttribute(string name) => name.Length == 12 && name.StartsWith("b-", StringComparison.Ordinal) && name.AsSpan(2).ContainsOnlyLowercaseAsciiLettersOrDigits();

    private static bool ContainsOnlyLowercaseAsciiLettersOrDigits(this ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }
}
