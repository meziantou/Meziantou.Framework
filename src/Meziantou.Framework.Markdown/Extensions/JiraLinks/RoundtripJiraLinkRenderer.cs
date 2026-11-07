using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.JiraLinks;

/// <summary>
/// A roundtrip renderer for a <see cref="JiraLink"/>, which writes the issue key instead of a Markdown link.
/// </summary>
internal sealed class RoundtripJiraLinkRenderer : RoundtripObjectRenderer<JiraLink>
{
    protected override void Write(RoundtripRenderer renderer, JiraLink obj)
    {
        renderer.Write(obj.ProjectKey);
        renderer.Write('-');
        renderer.Write(obj.Issue);
    }
}
