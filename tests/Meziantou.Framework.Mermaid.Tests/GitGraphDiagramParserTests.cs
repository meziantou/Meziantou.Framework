namespace Meziantou.Framework.Mermaid.Tests;

public sealed class GitGraphDiagramParserTests
{
    private static GitGraphDiagram Parse(string text) => (GitGraphDiagram)MermaidDocument.Parse(text).Content;

    [Fact]
    public void CommitsBranchesAndMerges()
    {
        #region Mermaid_GitGraph
        var diagram = (GitGraphDiagram)MermaidDocument.Parse("""
            gitGraph
              commit id: "init"
              branch develop
              commit id: "feature" tag: "beta"
              commit type: REVERSE
              checkout main
              commit type: HIGHLIGHT msg: "hotfix"
              merge develop id: "release" tag: "v1.0.0"
              branch support order: 2
              cherry-pick id: "feature"
            """).Content;
        #endregion

        Assert.HasCount(9, diagram.Operations);
        var commit = (GitGraphCommit)diagram.Operations[2];
        Assert.Equal(("feature", ["beta"]), (commit.Id, commit.Tags.ToArray()));
        Assert.Equal(GitGraphCommitType.Reverse, ((GitGraphCommit)diagram.Operations[3]).Type);
        var hotfix = (GitGraphCommit)diagram.Operations[5];
        Assert.Equal((GitGraphCommitType.Highlight, "hotfix"), (hotfix.Type, hotfix.Message));
        var merge = (GitGraphMerge)diagram.Operations[6];
        Assert.Equal(("develop", "release", "v1.0.0"), (merge.Branch, merge.Id, merge.Tags.Single()));
        Assert.Equal(("support", (int?)2), (((GitGraphBranch)diagram.Operations[7]).Name, ((GitGraphBranch)diagram.Operations[7]).Order));
        Assert.Equal("feature", ((GitGraphCherryPick)diagram.Operations[8]).CommitId);
    }

    [Theory]
    [InlineData("gitGraph\n  commit", GitGraphOrientation.LeftToRight)]
    [InlineData("gitGraph:\n  commit", GitGraphOrientation.LeftToRight)]
    [InlineData("gitGraph LR:\n  commit", GitGraphOrientation.LeftToRight)]
    [InlineData("gitGraph TB:\n  commit", GitGraphOrientation.TopToBottom)]
    [InlineData("gitGraph BT :\n  commit", GitGraphOrientation.BottomToTop)]
    public void Orientations(string text, GitGraphOrientation expected)
    {
        Assert.Equal(expected, Parse(text).Orientation);
    }

    [Fact]
    public void Configuration()
    {
        var diagram = Parse("""
            ---
            title: History
            config:
              gitGraph:
                mainBranchName: trunk
                mainBranchOrder: 2
                showBranches: false
                showCommitLabel: false
                rotateCommitLabel: false
                parallelCommits: true
              themeVariables:
                git0: "#ff0000"
                gitInv1: "#00ff00"
                gitBranchLabel2: "#0000ff"
                tagLabelBorder: "#123456"
            ---
            gitGraph
              accTitle: Accessible title
              commit
              switch trunk
            """);
        Assert.Equal(("History", "Accessible title", "trunk", 2.0), (diagram.Title, diagram.AccessibleTitle, diagram.MainBranchName, diagram.MainBranchOrder));
        Assert.Equal((false, false, false, true), (diagram.ShowBranches, diagram.ShowCommitLabels, diagram.RotateCommitLabels, diagram.ParallelCommits));
        Assert.Equal((SceneColor)"#ff0000", diagram.BranchColors[0]);
        Assert.Equal((SceneColor)"#00ff00", diagram.HighlightColors[1]);
        Assert.Equal((SceneColor)"#0000ff", diagram.BranchLabelColors[2]);
        Assert.Equal((SceneColor)"#123456", diagram.TagLabelBorder);
        Assert.Equal("trunk", ((GitGraphCheckout)diagram.Operations[1]).Branch);
    }

    [Fact]
    public void StringsAndTitle()
    {
        var diagram = Parse("""
            gitGraph
              title   The   history
              commit id: 'single \'quoted\'' msg: "line\nbreak"
              branch "with space"
              commit "message only"
              cherry-pick id: "single 'quoted'" tag: ""
            """);
        Assert.Equal("The history", diagram.Title);
        var commit = (GitGraphCommit)diagram.Operations[0];
        Assert.Equal(("single 'quoted'", "line\nbreak"), (commit.Id, commit.Message));
        Assert.Equal("with space", ((GitGraphBranch)diagram.Operations[1]).Name);
        Assert.Equal("message only", ((GitGraphCommit)diagram.Operations[2]).Message);
        Assert.False(((GitGraphCherryPick)diagram.Operations[3]).ShowDefaultTag);
    }

    [Fact]
    public void DuplicateCommitIsAWarning()
    {
        var document = MermaidDocument.Parse("gitGraph\n  commit id: \"a\"\n  commit id: \"a\"");
        Assert.Contains(document.Warnings, warning => warning.Message.Contains("already exists", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("gitGraph XY:\n  commit", "'XY' is not a direction")]
    [InlineData("gitGraph\n  checkout develop", "Trying to checkout branch 'develop', which is not yet created.")]
    [InlineData("gitGraph\n  commit\n  branch main", "Trying to create an existing branch 'main'.")]
    [InlineData("gitGraph\n  commit\n  merge main", "Cannot merge branch 'main' into itself.")]
    [InlineData("gitGraph\n  commit\n  merge develop", "The branch to merge (develop) does not exist.")]
    [InlineData("gitGraph\n  commit\n  branch develop\n  checkout main\n  merge develop", "Both branches have the same head.")]
    [InlineData("gitGraph\n  commit\n  cherry-pick id: \"unknown\"", "The source commit should exist and be provided.")]
    [InlineData("gitGraph\n  commit id: \"a\"\n  cherry-pick id: \"a\"", "The source commit is already on the current branch.")]
    [InlineData("gitGraph\n  commit type: OTHER", "'OTHER' is not a type of commit")]
    [InlineData("gitGraph\n  commit id: unquoted", "a quoted string is expected.")]
    [InlineData("gitGraph\n  commit\n  branch develop order: 01", "The order of a branch is a number.")]
    [InlineData("gitGraph\n  push", "a statement such as 'commit' or 'branch' is expected.")]
    public void InvalidGitGraphs(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new GitGraphDiagram
        {
            Title = "History",
            Theme = DiagramTheme.Forest,
            Orientation = GitGraphOrientation.BottomToTop,
            MainBranchName = "trunk",
            ParallelCommits = true,
            RotateCommitLabels = false,
        };
        diagram.BranchColors[3] = (SceneColor)"#ff0000";
        diagram.CommitLabelColor = (SceneColor)"#00ff00";
        diagram.Commit("first \"quoted\"", GitGraphCommitType.Highlight, "v0.1");
        diagram.Branch("feature/with space", order: 3);
        var commit = diagram.Commit();
        commit.Message = "multi\nline";
        diagram.Checkout("trunk");
        diagram.Commit("second");
        diagram.Merge("feature/with space", "merge").Type = GitGraphCommitType.Reverse;
        diagram.Branch("support");
        diagram.CherryPick("second").ShowDefaultTag = false;

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (GitGraphDiagram)parsed.Content;
        Assert.Equal(("History", GitGraphOrientation.BottomToTop, "trunk", true, false), (result.Title, result.Orientation, result.MainBranchName, result.ParallelCommits, result.RotateCommitLabels));
        Assert.Same(DiagramTheme.Forest, result.Theme);
        Assert.Equal((SceneColor)"#ff0000", result.BranchColors[3]);
        Assert.Equal((SceneColor)"#00ff00", result.CommitLabelColor);
        var first = (GitGraphCommit)result.Operations[0];
        Assert.Equal(("first \"quoted\"", GitGraphCommitType.Highlight, "v0.1"), (first.Id, first.Type, first.Tags.Single()));
        Assert.Equal(("feature/with space", (int?)3), (((GitGraphBranch)result.Operations[1]).Name, ((GitGraphBranch)result.Operations[1]).Order));
        Assert.Equal("multi\nline", ((GitGraphCommit)result.Operations[2]).Message);
        Assert.Equal(GitGraphCommitType.Reverse, ((GitGraphMerge)result.Operations[5]).Type);
        Assert.False(((GitGraphCherryPick)result.Operations[7]).ShowDefaultTag);
    }

    [Fact]
    public void WriteRejectsNegativeOrders()
    {
        var diagram = new GitGraphDiagram();
        diagram.Commit();
        diagram.Branch("develop", order: -1);
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(diagram));
    }
}
