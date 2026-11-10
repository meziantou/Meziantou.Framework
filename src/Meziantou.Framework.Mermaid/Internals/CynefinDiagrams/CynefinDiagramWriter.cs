using System.Globalization;
using System.Text;
using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Mermaid.Internals.CynefinDiagrams;

/// <summary>
/// Writes a <see cref="CynefinDiagram"/> as Mermaid: its configuration in the front matter, its colors in a directive,
/// then a block per domain with its items, in the order the domains first appear in the items, and the transitions.
/// </summary>
internal static class CynefinDiagramWriter
{
    public static void Write(StringBuilder builder, CynefinDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Width != 800)
        {
            configuration.Add("width: " + MermaidStyle.Number(diagram.Width));
        }

        if (diagram.Height != 600)
        {
            configuration.Add("height: " + MermaidStyle.Number(diagram.Height));
        }

        if (diagram.Padding != 40)
        {
            if (diagram.Padding > 1000)
                throw new ArgumentException("The padding of a Cynefin diagram cannot be greater than 1000 in Mermaid.", nameof(diagram));

            configuration.Add("padding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (!diagram.ShowDomainDescriptions)
        {
            configuration.Add("showDomainDescriptions: false");
        }

        if (diagram.BoundaryAmplitude != 8)
        {
            configuration.Add("boundaryAmplitude: " + MermaidStyle.Number(diagram.BoundaryAmplitude));
        }

        if (diagram.Seed != 0)
        {
            configuration.Add("seed: " + diagram.Seed.ToString(CultureInfo.InvariantCulture));
        }

        var theme = diagram.Colors;
        var variables = new List<string>();
        AddColor("complexBg", theme.ComplexBackground);
        AddColor("complicatedBg", theme.ComplicatedBackground);
        AddColor("clearBg", theme.ClearBackground);
        AddColor("chaoticBg", theme.ChaoticBackground);
        AddColor("confusionBg", theme.ConfusionBackground);
        AddColor("boundaryColor", theme.BoundaryColor);
        AddColor("cliffColor", theme.CliffColor);
        AddColor("arrowColor", theme.ArrowColor);
        AddColor("labelColor", theme.LabelColor);
        AddColor("textColor", theme.TextColor);

        // Mermaid reads 0 as "use the default" (value || default): a width of 0 cannot be written
        AddNumber("boundaryWidth", theme.BoundaryWidth);
        AddNumber("cliffWidth", theme.CliffWidth);
        AddNumber("arrowWidth", theme.ArrowWidth);
        AddNumber("domainFontSize", theme.DomainFontSize);
        AddNumber("itemFontSize", theme.ItemFontSize);

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "cynefin", configuration);
        if (variables.Count > 0)
        {
            builder.Append("%%{init: {\"themeVariables\": {\"cynefin\": {").Append(string.Join(", ", variables)).Append("}}}}%%\n");
        }

        builder.Append("cynefin-beta\n");
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        foreach (var domain in diagram.Items.Select(item => item.Domain).Distinct())
        {
            builder.Append("    ").Append(CynefinDiagramParser.GetName(domain)).Append('\n');
            foreach (var item in diagram.Items)
            {
                if (item.Domain == domain)
                {
                    builder.Append("        ").Append(GitGraphLangium.Quote(item.Label.Text)).Append('\n');
                }
            }
        }

        foreach (var transition in diagram.Transitions)
        {
            // Mermaid ignores a transition to the same domain
            if (transition.From == transition.To)
                continue;

            builder.Append("    ").Append(CynefinDiagramParser.GetName(transition.From)).Append(" --> ").Append(CynefinDiagramParser.GetName(transition.To));
            if (transition.Label is { IsEmpty: false } label)
            {
                builder.Append(" : ").Append(GitGraphLangium.Quote(label.Text));
            }

            builder.Append('\n');
        }

        void AddColor(string name, SceneColor? color)
        {
            if (color is { } value)
            {
                variables.Add("\"" + name + "\": \"" + value.ToString() + "\"");
            }
        }

        void AddNumber(string name, double? value)
        {
            if (value is { } number && number != 0)
            {
                variables.Add("\"" + name + "\": " + MermaidStyle.Number(number));
            }
        }
    }
}
