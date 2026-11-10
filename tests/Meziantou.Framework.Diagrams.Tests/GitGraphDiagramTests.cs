namespace Meziantou.Framework.Diagrams.Tests;

public sealed class GitGraphDiagramTests
{
    [Fact]
    public void GitGraph()
    {
        #region Diagrams_GitGraph
        var diagram = new GitGraphDiagram();
        diagram.Commit("init");
        diagram.Branch("develop");
        diagram.Commit("feature");
        diagram.Commit("fix", GitGraphCommitType.Reverse);
        diagram.Checkout("main");
        diagram.Commit("hotfix", GitGraphCommitType.Highlight);
        diagram.Merge("develop", "release").Tags.Add("v1.0.0");
        diagram.Branch("support");
        diagram.CherryPick("feature");
        diagram.Commit();
        #endregion

        DiagramAssert.Svg(diagram, "GitGraph");
    }

    [Fact]
    public void TopToBottom()
    {
        var diagram = CreateSample();
        diagram.Orientation = GitGraphOrientation.TopToBottom;
        DiagramAssert.Svg(diagram, "GitGraphTopToBottom");
    }

    [Fact]
    public void BottomToTop()
    {
        var diagram = CreateSample();
        diagram.Orientation = GitGraphOrientation.BottomToTop;
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void OptionsAndColors()
    {
        var diagram = CreateSample();
        diagram.Theme = DiagramTheme.Dark;
        diagram.ShowBranches = false;
        diagram.RotateCommitLabels = false;
        diagram.ParallelCommits = true;
        diagram.BranchColors[1] = (SceneColor)"#ff0000";
        diagram.CommitLabelBackground = (SceneColor)"#00ff00";
        var svg = diagram.ToSvg();
        Assert.Contains("#ff0000", svg, StringComparison.Ordinal);
        Assert.DoesNotContain(">develop<", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void BranchOrderAndMainBranch()
    {
        var diagram = new GitGraphDiagram { MainBranchName = "trunk", MainBranchOrder = 2 };
        diagram.Commit();
        diagram.Branch("first", order: 1);
        diagram.Commit();
        diagram.Checkout("trunk");
        diagram.Branch("third", order: 3);
        diagram.Commit();
        var svg = diagram.ToSvg();
        Assert.True(svg.IndexOf(">first<", StringComparison.Ordinal) < svg.IndexOf(">trunk<", StringComparison.Ordinal));
        Assert.True(svg.IndexOf(">trunk<", StringComparison.Ordinal) < svg.IndexOf(">third<", StringComparison.Ordinal));
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void InvalidOperationsAreSkipped()
    {
        var diagram = new GitGraphDiagram();
        diagram.Checkout("unknown");
        diagram.Merge("main");
        diagram.CherryPick("unknown");
        diagram.Commit("a");
        diagram.Commit("a");
        diagram.Branch("main");
        var svg = diagram.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void EmptyDiagram()
    {
        var svg = new GitGraphDiagram().ToSvg();
        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void MainBranchNameIsRequired()
    {
        Assert.Throws<ArgumentException>("value", () => new GitGraphDiagram { MainBranchName = "" });
    }

    private static GitGraphDiagram CreateSample()
    {
        var diagram = new GitGraphDiagram();
        diagram.Commit("a");
        diagram.Branch("develop");
        diagram.Commit("b", tags: "beta");
        diagram.Checkout("main");
        diagram.Commit("c");
        diagram.Merge("develop");
        diagram.Branch("feature");
        diagram.Commit("d", GitGraphCommitType.Highlight);
        diagram.Checkout("develop");
        diagram.Commit("e");
        diagram.Checkout("feature");
        diagram.Merge("develop");
        return diagram;
    }
}
