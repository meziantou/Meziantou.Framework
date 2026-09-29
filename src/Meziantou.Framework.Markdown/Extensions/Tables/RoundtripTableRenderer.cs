using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Tables;

/// <summary>
/// A roundtrip renderer for a <see cref="Table"/>.
/// </summary>
internal sealed class RoundtripTableRenderer : RoundtripObjectRenderer<Table>
{
    protected override void Write(RoundtripRenderer renderer, Table obj)
    {
        renderer.RenderLinesBefore(obj);
        if (obj.SourceLines is { } lines)
        {
            // A grid table is written as it is in the source
            foreach (var line in lines)
            {
                renderer.Write(line);
                renderer.WriteLine(line.NewLine);
            }
        }
        else
        {
            renderer.WriteChildren(obj);
        }

        renderer.RenderLinesAfter(obj);
    }
}
