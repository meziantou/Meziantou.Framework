using System.Globalization;
using System.Text;
using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Mermaid.Internals.PacketDiagrams;

/// <summary>Writes a <see cref="PacketDiagram"/> as Mermaid: a line per field, with its range of bits.</summary>
internal static class PacketDiagramWriter
{
    public static void Write(StringBuilder builder, PacketDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.BitsPerRow != 32)
        {
            configuration.Add("bitsPerRow: " + diagram.BitsPerRow.ToString(CultureInfo.InvariantCulture));
        }

        if (diagram.BitWidth != 32)
        {
            configuration.Add("bitWidth: " + MermaidStyle.Number(diagram.BitWidth));
        }

        if (diagram.RowHeight != 32)
        {
            configuration.Add("rowHeight: " + MermaidStyle.Number(diagram.RowHeight));
        }

        if (!diagram.ShowBits)
        {
            configuration.Add("showBits: false");
        }

        if (diagram.DescendingBitOrder)
        {
            configuration.Add("bitOrder: descending");
        }

        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "packet", configuration);
        builder.Append("packet\n");
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        var next = 0;
        foreach (var field in diagram.Fields)
        {
            if (field.Start != next)
                throw new ArgumentException($"The field {field.Start}-{field.End} is not contiguous: Mermaid needs it to start at {next}.", nameof(diagram));

            builder.Append("    ").Append(field.Start.ToString(CultureInfo.InvariantCulture));
            if (field.End != field.Start)
            {
                builder.Append('-').Append(field.End.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(": ").Append(GitGraphLangium.Quote(field.Label.Text)).Append('\n');
            next = field.End + 1;
        }
    }
}
