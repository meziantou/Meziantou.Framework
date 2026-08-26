using System.Reflection;

namespace Meziantou.Framework.Language.Regex.Tests;

public sealed class RegexSyntaxVisitorTests
{
    /// <summary>
    /// Every node type must be reachable through the visitor. A node whose <c>Accept</c> was never wired up would
    /// otherwise be invisible to every walker without anything failing.
    /// </summary>
    [Fact]
    public void EveryNodeTypeHasAVisitMethod()
    {
        var nodeTypes = typeof(RegexSyntaxNode).Assembly.GetTypes()
            .Where(type => type.IsSealed && !type.IsAbstract && typeof(RegexSyntaxNode).IsAssignableFrom(type))
            .ToArray();

        var visitMethods = typeof(RegexSyntaxVisitor)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name.StartsWith("Visit", StringComparison.Ordinal) && method.GetParameters().Length == 1)
            .Select(method => method.GetParameters()[0].ParameterType)
            .ToHashSet();

        Assert.NotEmpty(nodeTypes);
        foreach (var nodeType in nodeTypes)
        {
            Assert.Contains(nodeType, visitMethods, $"{nodeType.Name} has no Visit method");
        }
    }

    [Fact]
    public void VisitorReachesEveryNodeOfATree()
    {
        var tree = RegexSyntaxTree.ParseText(@"(?<n>a|[b-d])\k<n>{2,3}?", RegexFlavor.Net);
        var counter = new NodeCounter();

        counter.Visit(tree.Root);

        Assert.Equal(tree.Root.DescendantNodesAndSelf().Count(), counter.Count);
    }

    [Fact]
    public void TypedVisitorReturnsAValue()
    {
        var tree = RegexSyntaxTree.ParseText("a|b", RegexFlavor.Net);

        Assert.Equal(RegexSyntaxKind.Pattern, new KindReader().Visit(tree.Root));
    }

    [Fact]
    public void DescendantNodesAreReturnedInSourceOrder()
    {
        var tree = RegexSyntaxTree.ParseText("(ab)|c", RegexFlavor.Net);

        var starts = tree.Root.DescendantNodes().Select(node => node.FullSpan.Start).ToArray();

        Assert.Equal(starts.OrderBy(start => start), starts);
    }

    [Fact]
    public void DescendantTokensReproduceTheSource()
    {
        const string Pattern = @"(?<n>a|[b-d])\k<n>{2,3}?";
        var tree = RegexSyntaxTree.ParseText(Pattern, RegexFlavor.Net);

        Assert.Equal(Pattern, string.Concat(tree.Root.DescendantTokens().Select(token => token.ToFullString())));
    }

    private sealed class NodeCounter : RegexSyntaxVisitor
    {
        public int Count { get; private set; }

        protected override void DefaultVisit(RegexSyntaxNode node)
        {
            Count++;
            base.DefaultVisit(node);
        }
    }

    private sealed class KindReader : RegexSyntaxVisitor<RegexSyntaxKind>
    {
        protected override RegexSyntaxKind DefaultVisit(RegexSyntaxNode node) => node.Kind;
    }
}
