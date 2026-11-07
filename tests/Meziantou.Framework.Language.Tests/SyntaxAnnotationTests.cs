using Meziantou.Framework.Language.Tests.TestLanguage;

namespace Meziantou.Framework.Language.Tests;

public sealed class SyntaxAnnotationTests
{
    [Fact]
    public void TwoAnnotationsWithTheSameKindAndData_AreNotEqual()
    {
        var first = new SyntaxAnnotation("kind", "data");
        var second = new SyntaxAnnotation("kind", "data");

        Assert.NotEqual(first, second);
        Assert.True(first != second);
        var sameInstance = first;
        Assert.Equal(first, sameInstance);
        Assert.True(first == sameInstance);
    }

    [Fact]
    public void Annotation_CarriesItsKindAndData()
    {
        var annotation = new SyntaxAnnotation("kind", "data");

        Assert.Equal("kind", annotation.Kind);
        Assert.Equal("data", annotation.Data);
        Assert.Equal("kind: data", annotation.ToString());
        Assert.Null(new SyntaxAnnotation().Kind);
    }

    [Fact]
    public void WithAdditionalAnnotations_KeepsTheNodeTypeAndDoesNotChangeTheOriginal()
    {
        var annotation = new SyntaxAnnotation();
        var atom = TestSyntax.Atom("a");

        TestAtomSyntax annotated = atom.WithAdditionalAnnotations(annotation);

        Assert.True(annotated.HasAnnotation(annotation));
        Assert.False(atom.HasAnnotation(annotation));
        Assert.Equal("a", annotated.ToFullString());
        Assert.True(annotated.IsEquivalentTo(atom));
    }

    [Fact]
    public void WithoutAnnotations_RemovesOnlyWhatWasAsked()
    {
        var kept = new SyntaxAnnotation("keep");
        var removed = new SyntaxAnnotation("remove");
        var atom = TestSyntax.Atom("a").WithAdditionalAnnotations(kept, removed);

        var result = atom.WithoutAnnotations(removed);

        Assert.True(result.HasAnnotation(kept));
        Assert.False(result.HasAnnotation(removed));
        Assert.False(result.WithoutAnnotations("keep").HasAnnotation(kept));
    }

    [Fact]
    public void AnAnnotationOnADescendant_IsFoundFromTheRootAndReportedUpTheSpine()
    {
        var annotation = new SyntaxAnnotation("marker");
        var root = TestSyntax.ParseRoot("(a,b)");
        var target = Assert.IsType<TestListSyntax>(root.Value).Values[1];

        var updated = (TestRootSyntax)root.Green.WithSlots([target.WithAdditionalAnnotations(annotation).Green, root.Green.GetSlot(1)])!.CreateRed();

        // Every node between the root and the annotated one reports that something below it is annotated.
        Assert.True(updated.ContainsAnnotations);
        Assert.Equal([annotation], updated.GetAnnotatedNodes(annotation).Single().GetAnnotations());
        Assert.Equal("b", updated.GetAnnotatedNodes("marker").Single().ToString());
    }

    [Fact]
    public void GetAnnotatedNodes_FindsNothingInATreeWithoutAnnotations()
    {
        var root = TestSyntax.ParseRoot("(a,b)");

        Assert.False(root.ContainsAnnotations);
        Assert.Empty(root.GetAnnotatedNodes(new SyntaxAnnotation()));
        Assert.Empty(root.GetAnnotatedNodes("marker"));
    }

    [Fact]
    public void TokensAndTriviaCarryAnnotationsToo()
    {
        var annotation = new SyntaxAnnotation("marker");
        var token = TestSyntax.ParseRoot("(a)").GetFirstToken();

        var annotated = token.WithAdditionalAnnotations(annotation);

        Assert.True(annotated.HasAnnotation(annotation));
        Assert.True(annotated.HasAnnotations("marker"));
        Assert.False(token.HasAnnotation(annotation));
        Assert.Equal("(", annotated.Text);

        var trivia = TestSyntax.ParseRoot(" a").GetFirstToken().LeadingTrivia[0].WithAdditionalAnnotations(annotation);

        Assert.True(trivia.HasAnnotation(annotation));
        Assert.Equal(" ", trivia.ToString());
    }

    [Fact]
    public void CopyAnnotationsTo_MovesThemOntoAnotherNode()
    {
        var annotation = new SyntaxAnnotation();
        var source = TestSyntax.Atom("a").WithAdditionalAnnotations(annotation);

        var target = source.CopyAnnotationsTo(TestSyntax.Atom("b"));

        Assert.True(target.HasAnnotation(annotation));
        Assert.Equal("b", target.ToFullString());
    }
}
