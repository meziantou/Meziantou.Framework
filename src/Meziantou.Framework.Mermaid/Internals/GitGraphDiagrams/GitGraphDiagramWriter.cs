using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;

/// <summary>Writes a <see cref="GitGraphDiagram"/> as Mermaid: its configuration and its colors in the front matter, then its operations in order.</summary>
internal static class GitGraphDiagramWriter
{
    public static void Write(StringBuilder builder, GitGraphDiagram diagram)
    {
        var configuration = new List<string>();
        if (diagram.Padding != 8)
        {
            configuration.Add("diagramPadding: " + MermaidStyle.Number(diagram.Padding));
        }

        if (diagram.MainBranchName != "main")
        {
            configuration.Add("mainBranchName: " + MermaidWriterHelpers.YamlDoubleQuoted(diagram.MainBranchName));
        }

        if (diagram.MainBranchOrder != 0 && double.IsFinite(diagram.MainBranchOrder))
        {
            configuration.Add("mainBranchOrder: " + MermaidStyle.Number(diagram.MainBranchOrder));
        }

        if (!diagram.ShowBranches)
        {
            configuration.Add("showBranches: false");
        }

        if (!diagram.ShowCommitLabels)
        {
            configuration.Add("showCommitLabel: false");
        }

        if (!diagram.RotateCommitLabels)
        {
            configuration.Add("rotateCommitLabel: false");
        }

        if (diagram.ParallelCommits)
        {
            configuration.Add("parallelCommits: true");
        }

        var variables = new List<string>();
        AddColors("git", diagram.BranchColors);
        AddColors("gitInv", diagram.HighlightColors);
        AddColors("gitBranchLabel", diagram.BranchLabelColors);
        AddColor("commitLabelColor", diagram.CommitLabelColor);
        AddColor("commitLabelBackground", diagram.CommitLabelBackground);
        AddColor("tagLabelColor", diagram.TagLabelColor);
        AddColor("tagLabelBackground", diagram.TagLabelBackground);
        AddColor("tagLabelBorder", diagram.TagLabelBorder);
        MermaidWriterHelpers.WriteFrontMatter(builder, diagram, "gitGraph", configuration);
        if (variables.Count > 0)
        {
            // The colors of the branches are theme variables, which a directive sets
            builder.Append("%%{init: {\"themeVariables\": {").Append(string.Join(", ", variables)).Append("}}}%%\n");
        }

        builder.Append("gitGraph").Append(diagram.Orientation switch
        {
            GitGraphOrientation.TopToBottom => " TB:",
            GitGraphOrientation.BottomToTop => " BT:",
            _ => string.Empty,
        }).Append('\n');
        MermaidWriterHelpers.WriteAccessibility(builder, diagram, 1);
        foreach (var operation in diagram.Operations)
        {
            builder.Append("    ");
            switch (operation)
            {
                case GitGraphCommit commit:
                    builder.Append("commit");
                    if (commit.Id is not null)
                    {
                        builder.Append(" id: ").Append(GitGraphLangium.Quote(commit.Id));
                    }

                    if (commit.Message is not null)
                    {
                        builder.Append(" msg: ").Append(GitGraphLangium.Quote(commit.Message));
                    }

                    if (commit.Type is not GitGraphCommitType.Normal)
                    {
                        builder.Append(" type: ").Append(FormatType(commit.Type));
                    }

                    AppendTags(builder, commit.Tags);
                    break;
                case GitGraphBranch branch:
                    builder.Append("branch ").Append(FormatReference(branch.Name));
                    if (branch.Order is { } order)
                    {
                        if (order < 0)
                            throw new ArgumentException($"The order of the branch '{branch.Name}' cannot be negative in Mermaid.", nameof(diagram));

                        builder.Append(" order: ").Append(order.ToString(CultureInfo.InvariantCulture));
                    }

                    break;
                case GitGraphCheckout checkout:
                    builder.Append("checkout ").Append(FormatReference(checkout.Branch));
                    break;
                case GitGraphMerge merge:
                    builder.Append("merge ").Append(FormatReference(merge.Branch));
                    if (merge.Id is not null)
                    {
                        builder.Append(" id: ").Append(GitGraphLangium.Quote(merge.Id));
                    }

                    if (merge.Type is { } type)
                    {
                        builder.Append(" type: ").Append(FormatType(type));
                    }

                    AppendTags(builder, merge.Tags);
                    break;
                case GitGraphCherryPick cherryPick:
                    builder.Append("cherry-pick id: ").Append(GitGraphLangium.Quote(cherryPick.CommitId));
                    if (cherryPick.Parent is not null)
                    {
                        builder.Append(" parent: ").Append(GitGraphLangium.Quote(cherryPick.Parent));
                    }

                    if (cherryPick.Tags.Count > 0)
                    {
                        AppendTags(builder, cherryPick.Tags);
                    }
                    else if (!cherryPick.ShowDefaultTag)
                    {
                        // An empty tag removes the tag Mermaid adds
                        builder.Append(" tag: \"\"");
                    }

                    break;
                default:
                    throw new ArgumentException($"The operation '{operation.GetType().Name}' cannot be written as Mermaid.", nameof(diagram));
            }

            builder.Append('\n');
        }

        void AddColors(string prefix, IDictionary<int, SceneColor> colors)
        {
            foreach (var (index, color) in colors.OrderBy(item => item.Key))
            {
                if (index is < 0 or > 7)
                    throw new ArgumentException($"The color {prefix}{index} cannot be written as Mermaid: the branches have 8 colors.", nameof(diagram));

                AddColor(prefix + index.ToString(CultureInfo.InvariantCulture), color);
            }
        }

        void AddColor(string name, SceneColor? color)
        {
            if (color is { } value)
            {
                variables.Add("\"" + name + "\": \"" + value.ToString() + "\"");
            }
        }
    }

    private static void AppendTags(StringBuilder builder, IList<string> tags)
    {
        foreach (var tag in tags)
        {
            builder.Append(" tag: ").Append(GitGraphLangium.Quote(tag));
        }
    }

    private static string FormatType(GitGraphCommitType type) => type switch
    {
        GitGraphCommitType.Reverse => "REVERSE",
        GitGraphCommitType.Highlight => "HIGHLIGHT",
        _ => "NORMAL",
    };

    private static string FormatReference(string name) => GitGraphDiagramParser.IsReference(name) ? name : GitGraphLangium.Quote(name);
}
